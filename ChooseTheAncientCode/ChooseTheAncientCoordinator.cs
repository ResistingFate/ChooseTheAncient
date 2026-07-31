using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;
using ChooseTheAncient.ChooseTheAncientCode.Messages;
using ChooseTheAncient.ChooseTheAncientCode.Interop;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class ChooseTheAncientCoordinator
{

    private static readonly MethodInfo ClearScreensMethod =
        AccessTools.Method(typeof(RunManager), "ClearScreens")
        ?? throw new InvalidOperationException("Could not locate RunManager.ClearScreens.");


    private static readonly MethodInfo FadeInMethod =
        AccessTools.Method(typeof(RunManager), "FadeIn")
        ?? throw new InvalidOperationException("Could not locate RunManager.FadeIn.");

    private static object? InvokeRunManagerMethod(
        MethodInfo method,
        RunManager runManager,
        object?[]? arguments = null)
    /*
     * Invokes non-public RunManager methods through one checked path so reflected exceptions preserve their real stack traces.
     */
    {
        try
        {
            return method.Invoke(runManager, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvokeRunManagerVoid(MethodInfo method, RunManager runManager)
    /*
     * Invokes reflected RunManager methods that are expected to return void.
     */
    {
        object? result = InvokeRunManagerMethod(method, runManager);
        if (result != null)
        {
            throw new InvalidOperationException(
                $"{method.DeclaringType?.FullName}.{method.Name} returned {result.GetType().FullName}; expected void.");
        }
    }

    private static async Task InvokeRunManagerTaskAsync(
        MethodInfo method,
        RunManager runManager,
        params object?[] arguments)
    /*
     * Invokes reflected RunManager methods that are expected to return Task and validates the runtime return type.
     */
    {
        object? result = InvokeRunManagerMethod(method, runManager, arguments);
        if (result is not Task task)
        {
            throw new InvalidOperationException(
                $"{method.DeclaringType?.FullName}.{method.Name} returned {result?.GetType().FullName ?? "<null>"}; expected Task.");
        }

        await task;
    }

    private static readonly object StartupBootstrapSyncLock = new();

    private static bool StartupBootstrapStepHandlerRegistered;
    private static INetGameService? StartupBootstrapStepHandlerNetService;
    private static MessageHandlerDelegate<ChooseTheAncientStartupStepCompletedMessage>? StartupBootstrapStepHandler;
    private static ChooseTheAncientFlowState? ActiveStartupBootstrapFlow;
    private static RunState? ActiveStartupBootstrapRunState;
    private static HashSet<ulong> ActiveStartupBootstrapPlayers = new();

    // This is a watchdog, not the normal path. The normal path proceeds as soon as every player reports.
    // It prevents a missed custom message from freezing the Act 1 shell room forever. Comment out if not needed.
    private const int StartupBootstrapBarrierMaxFrames = 7200;



    private static int BeginAct1StartupBootstrapSync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        IReadOnlyList<Player> orderedPlayers)
    /*
     * Starts a multiplayer barrier for the Act 1 bootstrap shell so all players complete modifier setup before map generation continues.
     */
    {
        EnsureStartupBootstrapStepHandlerRegistered(runState, flow, orderedPlayers);
        int syncEpoch = flow.BeginAct1StartupBootstrapSyncEpoch();

        ModLog.Debug(
            $"Began CTA Act 1 startup bootstrap sync epoch {syncEpoch} for players " +
            $"{string.Join(",", orderedPlayers.Select(player => player.NetId))}.");

        return syncEpoch;
    }





    public static async Task RunAct1StartingRoomFlowAsync(
        RunState runState,
        ChooseTheAncientFlowState flow)
    /*
     * Runs CTA's Act 1 starting-room replacement flow, including modifier bootstrap, ballot construction, voting, and room transition.
     */
    {
        ChooseTheAncientSelectionScreen? localScreen = null;

        try
        {
            if (!ChooseTheAncientHelpers.IsAct1StartingMapPoint(runState))
            {
                ModLog.Debug("Act 1 starting-room flow aborted because the player was no longer at the starting map point.");
                return;
            }

            if (runState.CurrentRoom is not ChooseTheAncientStartRoom)
            {
                ModLog.Warn("Act 1 starting-room flow expected the current room to be the ChooseTheAncient custom shell room.");
                return;
            }

            ChooseTheAncientConfig.RefreshFromNativeSettings();

            List<Player> orderedPlayers = runState.Players
                .OrderBy(runState.GetPlayerSlotIndex)
                .ToList();

            int startupBootstrapSyncEpoch = BeginAct1StartupBootstrapSync(runState, flow, orderedPlayers);

            int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);
            ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers);
            bool enableRedundantSettings =
                await GetEffectiveEnableRedundantSettingsAsync(orderedPlayers);
            IReadOnlyList<int>? effectiveAncientPoolSourceActs = enableRedundantSettings
                ? await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, targetActIndex: 0)
                : null;
            IReadOnlyDictionary<string, bool>? effectiveSpecialAncientOverrides = enableRedundantSettings
                ? await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, targetActIndex: 0)
                : null;

            ModLog.Info(
                "Act 1 starting-room flow effective settings: " +
                $"AncientCount={ancientCount}, GameMode={gameMode}, " +
                $"EnableRedundantSettings={enableRedundantSettings}, " +
                $"SourceActs={(enableRedundantSettings ? ChooseTheAncientConfig.DescribeAncientPoolSourceActs(effectiveAncientPoolSourceActs ?? Array.Empty<int>()) : "(disabled)")}, " +
                $"SpecialAncients={(enableRedundantSettings ? ChooseTheAncientConfig.DescribeSpecialAncientOverrides(effectiveSpecialAncientOverrides) : "(disabled)")}.");

            ActModel firstAct = runState.Acts[0];
            List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
                firstAct,
                runState,
                targetActIndex: 0,
                enabledSourceActsOverride: effectiveAncientPoolSourceActs,
                specialAncientOverridesOverride: effectiveSpecialAncientOverrides,
                enableRedundantSettingsOverride: enableRedundantSettings);

            pool = await GetEffectiveBallotAsync(
                runState,
                targetActIndex: 0,
                orderedPlayers,
                pool,
                ancientCount);

            if (pool.Count == 0)
            {
                AncientEventModel vanillaAncient = ChooseTheAncientHelpers.ResolveVanillaAct1FallbackAncient(firstAct, runState);
                ModLog.Warn(
                    $"Act 1 starting ancient ballot is empty in the starting-room flow. Falling back to vanilla ancient {vanillaAncient.Id.Entry}.");

                await WaitForAct1AutoResolveUiReadyAsync(
                    orderedPlayers,
                    vanillaAncient,
                    "empty Act 1 ballot fallback");

                ChooseTheAncientHelpers.SetChosenAncient(firstAct, vanillaAncient);
                await RunModifierBootstrapAsync(runState, flow, orderedPlayers, startupBootstrapSyncEpoch);
                flow.ModifierBootstrapCompleted = true;
                SetForceNeowBlessingModeIfNeeded(flow, vanillaAncient, "empty Act 1 ballot fallback");
                flow.ResolvedActs.Add(0);
                ChooseTheAncientHelpers.ConvertAct1StartShellToChosenAncient(runState, vanillaAncient);
                DispatchAncientRoomTransition(vanillaAncient, "empty Act 1 ballot fallback");
                ModLog.Info(
                    $"Act 1 starting-room flow dispatched the vanilla fallback ancient room transition for {vanillaAncient.Id.Entry}.");
                return;
            }

            ChooseTheAncientHelpers.LogPool("Act 1 starting-room ballot", pool);
            ModLog.Info($"Using game mode {gameMode} for the Act 1 starting-room flow.");

            AncientEventModel chosen;
            if (pool.Count == 1)
            {
                chosen = pool[0];
                ModLog.Info(
                    $"Only one starting ancient is available for Act 1: {chosen.Id.Entry}. " +
                    "Skipping the Act 1 selection screen and applying the choice directly.");

                await WaitForAct1AutoResolveUiReadyAsync(
                    orderedPlayers,
                    chosen,
                    "single-option Act 1 ballot");
            }
            else
            {
                Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
                if (localPlayer != null)
                {
                    ModLog.Info("Warming Act 1 ancient ballot visuals before opening the starting-room selection screen.");
                    await ChooseTheAncientHelpers.WarmAncientVisualAssetsAsync(pool);
                    await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);
                }

                (chosen, localScreen) = await RunAncientSelectionBallotAsync(
            runState,
            0,
            orderedPlayers,
            pool,
            gameMode);
    }

    ChooseTheAncientHelpers.SetChosenAncient(firstAct, chosen);

    ModLog.Info($"Chosen starting ancient for Act 1 from starting-room flow: {chosen.Id.Entry}");

            await RunModifierBootstrapAsync(runState, flow, orderedPlayers, startupBootstrapSyncEpoch);
            flow.ModifierBootstrapCompleted = true;
            SetForceNeowBlessingModeIfNeeded(flow, chosen, "Act 1 choice resolved");

            flow.ResolvedActs.Add(0);

            ChooseTheAncientHelpers.ConvertAct1StartShellToChosenAncient(runState, chosen);

            localScreen?.CloseScreen();
            localScreen = null;

            DispatchAncientRoomTransition(chosen, "Act 1 choice resolved");
            ModLog.Info($"Act 1 starting-room flow dispatched the chosen ancient room transition for {chosen.Id.Entry}.");
        }
        catch (OperationCanceledException ex)
        {
            ModLog.Warn(
                $"Act 1 starting-room flow canceled: {ex.GetType().Name}. " +
                "Leaving the current starting room in place.");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Act 1 starting-room flow failed: {ex}");
        }
        finally
        {
            localScreen?.CloseScreen();
            ReleaseStartupBootstrapStepHandlerContext(flow);
            flow.FlowInProgress = false;
            if (!flow.ResolvedActs.Contains(0))
            {
                flow.Act1StartingRoomFlowTriggered = false;
            }
            ModLog.Info(
                $"Act 1 starting-room flow cleanup. " +
                $"InProgress={flow.FlowInProgress}, Triggered={flow.Act1StartingRoomFlowTriggered}, " +
                $"ModifierBootstrapCompleted={flow.ModifierBootstrapCompleted}");
        }
    }


    private static async Task WaitForAct1AutoResolveUiReadyAsync(
        IReadOnlyList<Player> orderedPlayers,
        AncientEventModel chosenAncient,
        string reason)
    {
        Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
        if (localPlayer == null)
        {
            ModLog.Debug(
                $"Act 1 auto-resolve UI wait skipped for {chosenAncient.Id.Entry} because there is no local selecting player. " +
                $"Reason={reason}");
            return;
        }

        ModLog.Info(
            $"Waiting for the Act 1 CTA overlay-ready path before auto-resolving {chosenAncient.Id.Entry}. " +
            $"Reason={reason}");

        await ChooseTheAncientSelectionScreen.WaitForOverlayReadyWithoutInteractionAsync(
            nextActIndex: 0,
            orderedPlayers,
            extraFrames: 2);

        ModLog.Info(
            $"Act 1 CTA overlay-ready wait completed before auto-resolving {chosenAncient.Id.Entry}. " +
            $"Reason={reason}");
    }

    private static void DispatchAncientRoomTransition(AncientEventModel chosenAncient, string reason)
    {
        ModLog.Info(
            $"Dispatching Act 1 ancient room transition for {chosenAncient.Id.Entry}. Reason={reason}");

        _ = TransitionToAncientRoomAsync(chosenAncient);
    }

    private static async Task TransitionToAncientRoomAsync(AncientEventModel chosenAncient)
    {
        try
        {
            RunManager runManager = RunManager.Instance;
            RunState? runState = ChooseTheAncientHelpers.GetRunState(runManager);
            bool isShellRoomTransition = runState?.CurrentRoom is ChooseTheAncientStartRoom;

            ModLog.Info(
                $"Beginning Act 1 ancient room transition for {chosenAncient.Id.Entry}. " +
                $"ShellRoomTransition={isShellRoomTransition}.");

            if (isShellRoomTransition)
            {
                ModLog.Debug(
                    "Bypassing normal room exit/fade for the ChooseTheAncient start shell room and entering the chosen ancient room without exiting the current room first.");

                InvokeRunManagerVoid(ClearScreensMethod, runManager);
                await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

                ModLog.Info(
                    $"Entering Act 1 chosen ancient room for {chosenAncient.Id.Entry} without exiting the custom shell room first.");
                await runManager.EnterRoomWithoutExitingCurrentRoom(new EventRoom(chosenAncient), fadeToBlack: false);

                if (runState != null)
                {
                    ChooseTheAncientHelpers.ConvertAct1StartShellToChosenAncient(runState, chosenAncient);
                }

                ModLog.Info(
                    $"Completed Act 1 shell-room bypass transition for {chosenAncient.Id.Entry}.");
                return;
            }

            if (NGame.Instance?.Transition != null)
            {
                ModLog.Debug("Running Act 1 room fade out before entering the chosen ancient room.");
                await NGame.Instance.Transition.RoomFadeOut();
            }

            InvokeRunManagerVoid(ClearScreensMethod, runManager);
            await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

            ModLog.Info($"Entering Act 1 chosen ancient room for {chosenAncient.Id.Entry}.");
            await runManager.EnterRoom(new EventRoom(chosenAncient));

            await InvokeRunManagerTaskAsync(FadeInMethod, runManager, true);

            ModLog.Info($"Completed Act 1 ancient room transition for {chosenAncient.Id.Entry}.");
        }
        catch (OperationCanceledException ex)
        {
            ModLog.Warn(
                $"Act 1 ancient room transition for {chosenAncient.Id.Entry} was canceled: {ex.GetType().Name}.");
            throw;
        }
        catch (Exception ex)
        {
            ModLog.Error($"Act 1 ancient room transition for {chosenAncient.Id.Entry} failed: {ex}");
            throw;
        }
    }

    private static void SetForceNeowBlessingModeIfNeeded(
        ChooseTheAncientFlowState flow,
        AncientEventModel chosenAncient,
        string reason)
    /*
     * Enables the Neow blessing-mode compatibility flag when CTA selected Neow outside vanilla's normal Act 1 entry path.
     */
    {
        if (!ChooseTheAncientHelpers.IsNeowAncient(chosenAncient))
            return;

        flow.ForceNeowBlessingMode = true;
        ModLog.Info(
            $"ForceNeowBlessingMode enabled because CTA selected Neow ({chosenAncient.Id.Entry}). " +
            $"Reason={reason}");
    }


    public static async Task RunAsync(
        RunManager runManager,
        RunState runState,
        int nextActIndex,
        ChooseTheAncientFlowState flow)
    {
        ChooseTheAncientSelectionScreen? localScreen = null;
        bool enterNextActStarted = false;
        bool shouldEnterNextAct = false;
        int actIndexBeforeTransition = runState.CurrentActIndex;

        try
        {
            // Resolve the actual indexed ActModel before reading ballot settings.
            // A missing index means EnterNextAct is handling The Architect or
            // run completion, not entering another act.
            if (!TryResolveTargetAct(runState, nextActIndex, out ActModel? nextAct, out int resolvedActModelIndex))
            {
                ModLog.Info(
                    $"Skipping CTA flow for act {nextActIndex + 1} because no indexed ActModel exists. " +
                    $"RunState.Acts.Count={runState.Acts.Count}. Continuing vanilla EnterNextAct().");
                shouldEnterNextAct = true;
            }
            else
            {
                List<Player> orderedPlayers = runState.Players
                    .OrderBy(runState.GetPlayerSlotIndex)
                    .ToList();

                int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);
                ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers);
                bool enableRedundantSettings =
                    await GetEffectiveEnableRedundantSettingsAsync(orderedPlayers);
                IReadOnlyList<int>? effectiveAncientPoolSourceActs = enableRedundantSettings
                    ? await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, nextActIndex)
                    : null;
                IReadOnlyDictionary<string, bool>? effectiveSpecialAncientOverrides = enableRedundantSettings
                    ? await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, nextActIndex)
                    : null;

                List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
                    nextAct,
                    runState,
                    nextActIndex,
                    effectiveAncientPoolSourceActs,
                    effectiveSpecialAncientOverrides,
                    enableRedundantSettings);

                if (ModLog.IsDebugEnabled)
                {
                    string ancientPool = string.Join(",", pool.Select(ancient => ancient.Id.Entry));
                    ModLog.Debug($"Available ancients to draw {ancientCount} from for act {nextActIndex + 1}: {ancientPool}");
                }

                pool = await GetEffectiveBallotAsync(
                    runState,
                    nextActIndex,
                    orderedPlayers,
                    pool,
                    ancientCount);
                ChooseTheAncientHelpers.LogPool($"Act {nextActIndex + 1} initial ballot", pool);
                ModLog.Info($"Using game mode {gameMode} for act {nextActIndex + 1}.");

                AncientEventModel? chosen = null;
                if (pool.Count == 0)
                {
                    ModLog.Warn($"No ancient candidates remained for act {nextActIndex + 1}. Falling back to vanilla EnterNextAct().");
                }
                else if (pool.Count == 1)
                {
                    chosen = pool[0];
                    ChooseTheAncientHelpers.SetChosenAncient(nextAct, chosen);
                    ModLog.Info(
                        $"Only one ancient available for act {nextActIndex + 1}: {chosen.Id.Entry}. " +
                        $"ResolvedActModelIndex={resolvedActModelIndex + 1}.");
                }
                else
                {
                    (chosen, localScreen) = await RunAncientSelectionBallotAsync(
                        runState,
                        nextActIndex,
                        orderedPlayers,
                        pool,
                        gameMode);

                    ChooseTheAncientHelpers.SetChosenAncient(nextAct, chosen);
                    ModLog.Info(
                        $"Chosen ancient for act {nextActIndex + 1}: {chosen.Id.Entry}. " +
                        $"ResolvedActModelIndex={resolvedActModelIndex + 1}.");
                }

                if (chosen != null)
                {
                    SetForceNeowBlessingModeIfNeeded(flow, chosen, $"Act {nextActIndex + 1} choice resolved");
                }

                flow.ResolvedActs.Add(nextActIndex);
                shouldEnterNextAct = true;
            }
        }
        catch (OperationCanceledException ex)
        {
            ModLog.Warn(
                $"Ancient selection flow canceled for act {nextActIndex + 1}: " +
                $"{ex.GetType().Name}. Skipping forced act progression.");
            shouldEnterNextAct = false;
        }
        catch (Exception ex)
        {
            ModLog.Error(
                $"Ancient selection flow failed before EnterNextAct for act {nextActIndex + 1}: {ex}. " +
                "Falling back to vanilla EnterNextAct once.");
            shouldEnterNextAct = true;
        }

        try
        {
            if (shouldEnterNextAct)
            {
                localScreen?.CloseScreen();
                localScreen = null;

                ModLog.Debug(
                    $"Preparing CTA screen state before EnterNextAct for act {nextActIndex + 1}. " +
                    "Closing only the CTA screen, then waiting one process frame.");

                // Intentionally clear all RunManager screens before continuing so the
                // completed boss room is not briefly shown behind the transition.
                // Some endless mods reuse map-screen nodes across act cycles and may be
                // incompatible with this cleanup, but vanilla transitions expect a clean screen stack.
                InvokeRunManagerVoid(ClearScreensMethod, runManager); 
                await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

                enterNextActStarted = true;
                flow.ContinueEnterNextAct = true;
                await runManager.EnterNextAct();
            }
        }
        catch (OperationCanceledException ex)
        {
            ModLog.Warn(
                $"EnterNextAct canceled for act {nextActIndex + 1} after CTA flow: " +
                $"{ex.GetType().Name}. Not retrying.");
            throw;
        }
        catch (Exception ex)
        {
            ModLog.Error(
                $"EnterNextAct failed for act {nextActIndex + 1} after the transition had started. " +
                $"CurrentActIndexBefore={actIndexBeforeTransition + 1}, CurrentActIndexNow={runState.CurrentActIndex + 1}, " +
                $"RunState.Acts.Count={runState.Acts.Count}. Not retrying because retrying can skip an act: {ex}");
            throw;
        }
        finally
        {
            localScreen?.CloseScreen();
            flow.FlowInProgress = false;
            flow.ContinueEnterNextAct = false;
            ModLog.Info(
                $"Ancient flow cleanup. InProgress={flow.FlowInProgress}, ContinueNext={flow.ContinueEnterNextAct}, " +
                $"EnterNextActStarted={enterNextActStarted}");
        }
    }

    private static bool TryResolveTargetAct(
        RunState runState,
        int nextActIndex,
        [NotNullWhen(true)] out ActModel? targetAct,
        out int resolvedActModelIndex)
    /*
     * CTA only opens for an ActModel that actually exists at nextActIndex.
     * Endless or extended-act mods remain supported when they add their real
     * ActModel entries to RunState.Acts before requesting the transition.
     */
    {
        targetAct = null;
        resolvedActModelIndex = -1;

        if (nextActIndex < 0 || nextActIndex >= runState.Acts.Count)
        {
            return false;
        }

        ActModel? directAct = runState.Acts[nextActIndex];
        if (directAct == null)
        {
            ModLog.Warn(
                $"Cannot resolve target act {nextActIndex + 1}: runState.Acts[{nextActIndex}] is null. " +
                $"RunState.Acts.Count={runState.Acts.Count}.");
            return false;
        }

        targetAct = directAct;
        resolvedActModelIndex = nextActIndex;
        return true;
    }

    private static async Task<(AncientEventModel Chosen, ChooseTheAncientSelectionScreen? LocalScreen)> RunAncientSelectionBallotAsync(
        RunState runState,
        int targetActIndex,
        IReadOnlyList<Player> orderedPlayers,
        List<AncientEventModel> pool,
        ChooseTheAncientConfig.SelectionGameMode gameMode)
    {
        if (pool.Count == 0)
        {
            ModLog.Error(
                $"RunAncientSelectionBallotAsync was called for act {targetActIndex + 1} with an empty pool. " +
                "The selection screen will not be opened.");
            throw new InvalidOperationException($"RunAncientSelectionBallotAsync was called for act {targetActIndex + 1} with an empty pool.");
        }

        if (pool.Count == 1)
        {
            ModLog.Warn(
                $"RunAncientSelectionBallotAsync was called for act {targetActIndex + 1} with a single-option pool. " +
                $"Returning {pool[0].Id.Entry} without opening the selection screen.");
            return (pool[0], null);
        }

        ModLog.Info(
            $"Opening ChooseTheAncient selection screen for act {targetActIndex + 1} with {pool.Count} candidates: " +
            $"{ChooseTheAncientHelpers.DescribeAncients(pool)}");

        Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
        ChooseTheAncientSelectionScreen? localScreen = null;

        if (localPlayer != null)
        {
            localScreen = ChooseTheAncientSelectionScreen.Show(targetActIndex, orderedPlayers);
        }

        bool useSecondRound = gameMode is
            ChooseTheAncientConfig.SelectionGameMode.MontyHall or
            ChooseTheAncientConfig.SelectionGameMode.FairFight;

        AncientEventModel chosen;
        if (!useSecondRound)
        {
            bool enablePreviews = gameMode == ChooseTheAncientConfig.SelectionGameMode.WantToKnowEverything;

            Dictionary<string, ChooseTheAncientHelpers.AncientPreviewData>? localPreviewData = null;
            if (enablePreviews && localPlayer != null)
            {
                localPreviewData = ChooseTheAncientHelpers.BuildPreviewDataByAncientId(
                    localPlayer,
                    pool,
                    targetActIndex);
            }

            var singleRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                pool,
                enablePreviews
                    ? ChooseTheAncientSelectionScreen.VoteRoundType.FinalRevealVote
                    : ChooseTheAncientSelectionScreen.VoteRoundType.InitialKeepVote,
                localPreviewData,
                null,
                null,
                null,
                null);

            List<int> singleRoundVotes = await CollectVotes(
                orderedPlayers,
                singleRound,
                localScreen);

            int chosenIndex = ResolveMostVotedIndex(
                runState,
                targetActIndex,
                pool.Count,
                singleRoundVotes);

            if (localScreen != null)
            {
                if (enablePreviews)
                {
                    await localScreen.PlayFinalVoteResolutionAsync(singleRoundVotes, chosenIndex);
                }
                else
                {
                    await localScreen.PlayInitialVoteResolutionAsync(singleRoundVotes, chosenIndex);
                }
            }

            chosen = pool[chosenIndex];
        }
        else
        {
            List<AncientEventModel> finalists = pool;

            var firstRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                pool,
                ChooseTheAncientSelectionScreen.VoteRoundType.InitialKeepVote,
                null,
                null,
                null,
                null,
                null);

            List<int> firstVotes = await CollectVotes(
                orderedPlayers,
                firstRound,
                localScreen);

            int firstPlaceIndex = ResolveMostVotedIndex(
                runState,
                targetActIndex,
                pool.Count,
                firstVotes);

            int secondPlaceIndex = ResolveSecondPlaceIndex(
                runState,
                targetActIndex,
                pool,
                firstPlaceIndex,
                firstVotes);

            AncientEventModel firstAncient = pool[firstPlaceIndex];
            AncientEventModel secondAncient = pool[secondPlaceIndex];

            if (localScreen != null)
            {
                await localScreen.PlayInitialVoteResolutionAsync(firstVotes, firstPlaceIndex);
            }

            string finalistSignature = string.Join(
                "|",
                new[] { firstAncient.Id.Entry, secondAncient.Id.Entry }
                    .OrderBy(id => id, StringComparer.Ordinal));

            var secondRoundDisplayRng = ChooseTheAncientHelpers.CreateRunScopedRng(
                runState,
                "second_round_finalist_display",
                "act",
                targetActIndex,
                "finalists",
                finalistSignature,
                "votes",
                string.Join(",", firstVotes));

            finalists = BuildSecondRoundFinalistDisplayOrder(
                [firstAncient, secondAncient],
                ancient => ancient.Id.Entry,
                secondRoundDisplayRng.Shuffle);

            ModLog.Info(
                $"First-pass elimination kept {firstAncient.Id.Entry}, {secondAncient.Id.Entry}; " +
                $"second-round display order is {string.Join(", ", finalists.Select(ancient => ancient.Id.Entry))}.");
            ChooseTheAncientHelpers.LogPool($"Act {targetActIndex + 1} finalists", finalists);

            Dictionary<string, ChooseTheAncientHelpers.AncientPreviewData>? localPreviewData = null;
            if (localPlayer != null)
            {
                localPreviewData = ChooseTheAncientHelpers.BuildPreviewDataByAncientId(
                    localPlayer,
                    finalists,
                    targetActIndex);
            }

            (AncientEventModel? suppressedPreviewAncient, AncientEventModel? reactionAncient, string? suppressedPreviewAncientId, string? reactionAncientId) = ResolveSecondRoundPresentation(
                runState,
                targetActIndex,
                pool,
                finalists,
                firstVotes,
                firstAncient.Id.Entry);

            var secondRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                finalists,
                ChooseTheAncientSelectionScreen.VoteRoundType.FinalRevealVote,
                localPreviewData,
                suppressedPreviewAncientId,
                suppressedPreviewAncient,
                reactionAncientId,
                reactionAncient,
                HideSuppressedPreview:
                    gameMode == ChooseTheAncientConfig.SelectionGameMode.MontyHall,
                HideSuppressedDialogue:
                    gameMode == ChooseTheAncientConfig.SelectionGameMode.MontyHall);

            List<int> finalVotes = await CollectVotes(
                orderedPlayers,
                secondRound,
                localScreen);

            int chosenIndex = ResolveMostVotedIndex(
                runState,
                targetActIndex,
                finalists.Count,
                finalVotes);

            if (localScreen != null)
            {
                await localScreen.PlayFinalVoteResolutionAsync(finalVotes, chosenIndex);
            }

            chosen = finalists[chosenIndex];
        }

        return (chosen, localScreen);
    }

    
    private static async Task RunModifierBootstrapAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        IReadOnlyList<Player> orderedPlayers,
        int syncEpoch)
    /*
     * Applies custom-game modifier bootstrap actions before CTA builds the Act 1 selection.
     */
    {
        EnsureStartupBootstrapStepHandlerRegistered(runState, flow, orderedPlayers);

        Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);

        if (localPlayer != null)
        {
            List<ChooseTheAncientHelpers.ModifierBootstrapAction> bootstrapActions =
                OrderModifierBootstrapActions(
                    ChooseTheAncientHelpers.BuildModifierBootstrapActions(localPlayer));

            if (bootstrapActions.Count > 0)
            {
                ModLog.Info(
                    $"Running {bootstrapActions.Count} start-of-run modifier bootstrap action(s) " +
                    $"for local player {localPlayer.NetId}. " +
                    $"Order={string.Join(", ", bootstrapActions.Select(action => $"{GetModifierBootstrapId(action)}@{GetModifierBootstrapRunOrderIndex(action)}"))}.");

                for (int stepIndex = 0; stepIndex < bootstrapActions.Count; stepIndex++)
                {
                    ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction = bootstrapActions[stepIndex];
                    string modifierId = GetModifierBootstrapId(bootstrapAction);

                    ModLog.Info(
                        $"Running modifier bootstrap step {stepIndex + 1}/{bootstrapActions.Count} for {modifierId}.");
                    await bootstrapAction.ApplyAsync();
                    await ChooseTheAncientHelpers.WaitForProcessFramesAsync(2);

                    await SyncModifierBootstrapStepCompletionAsync(
                        runState,
                        flow,
                        orderedPlayers,
                        localPlayer,
                        syncEpoch,
                        stepIndex,
                        bootstrapActions.Count,
                        modifierId);
                }
            }
            else
            {
                ModLog.Info($"No start-of-run modifier bootstrap actions were found for local player {localPlayer.NetId}.");
            }
        }

        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(2);
    }

    private static void EnsureStartupBootstrapStepHandlerRegistered(
        RunState runState,
        ChooseTheAncientFlowState flow,
        IReadOnlyList<Player> orderedPlayers)
    /*
     * Registers the temporary network message handler used to track Act 1 bootstrap step completion.
     */
    {
        RunManager runManager = RunManager.Instance;
        INetGameService netService = runManager.NetService;

        lock (StartupBootstrapSyncLock)
        {
            ActiveStartupBootstrapRunState = runState;
            ActiveStartupBootstrapFlow = flow;
            ActiveStartupBootstrapPlayers = orderedPlayers.Select(player => player.NetId).ToHashSet();

            if (StartupBootstrapStepHandlerRegistered && !ReferenceEquals(StartupBootstrapStepHandlerNetService, netService))
            {
                if (StartupBootstrapStepHandlerNetService != null && StartupBootstrapStepHandler != null)
                {
                    StartupBootstrapStepHandlerNetService.UnregisterMessageHandler<ChooseTheAncientStartupStepCompletedMessage>(StartupBootstrapStepHandler);
                }

                StartupBootstrapStepHandlerRegistered = false;
                StartupBootstrapStepHandlerNetService = null;
                StartupBootstrapStepHandler = null;
            }

            if (!StartupBootstrapStepHandlerRegistered)
            {
                StartupBootstrapStepHandler = HandleStartupBootstrapStepCompletedMessage;
                netService.RegisterMessageHandler<ChooseTheAncientStartupStepCompletedMessage>(StartupBootstrapStepHandler);
                StartupBootstrapStepHandlerNetService = netService;
                StartupBootstrapStepHandlerRegistered = true;
            }
        }
    }

    private static void ReleaseStartupBootstrapStepHandlerContext(
        ChooseTheAncientFlowState flow)
    /*
     * Clears the active bootstrap sync context after the Act 1 startup barrier is no longer needed.
     */
    {
        lock (StartupBootstrapSyncLock)
        {
            if (!ReferenceEquals(ActiveStartupBootstrapFlow, flow))
                return;

            if (flow.ModifierBootstrapCompleted)
            {
                // Keep the handler/context alive after the shell-room transition is dispatched.
                // A late startup-completion message can still arrive before the first NEOW checksum;
                // unregistering here was what made those late messages unhandleable.
                ModLog.Debug("Keeping CTA startup bootstrap handler context available for late completion messages.");
                return;
            }

            ActiveStartupBootstrapFlow = null;
            ActiveStartupBootstrapRunState = null;
            ActiveStartupBootstrapPlayers = new HashSet<ulong>();

            // Keep the net message handler registered. With a null context it becomes a harmless no-op,
            // but late reliable messages no longer produce 'no handlers are registered' warnings.
        }
    }

    private static void HandleStartupBootstrapStepCompletedMessage(
        ChooseTheAncientStartupStepCompletedMessage message,
        ulong senderId)
    /*
     * Handles a remote player's Act 1 bootstrap step-complete message and advances the shared barrier state.
     */
    {
        RunState? runStateForAlignment = null;
        bool shouldAlign = false;

        lock (StartupBootstrapSyncLock)
        {
            ChooseTheAncientFlowState? flow = ActiveStartupBootstrapFlow;
            RunState? runState = ActiveStartupBootstrapRunState;
            bool senderTracked = ActiveStartupBootstrapPlayers.Count == 0 || ActiveStartupBootstrapPlayers.Contains(senderId);

            if (flow == null || runState == null || !senderTracked)
                return;

            if (message.syncEpoch != flow.Act1StartupBootstrapSyncEpoch)
            {
                ModLog.Debug(
                    $"Ignoring stale CTA startup step completion from player {senderId}. " +
                    $"MessageEpoch={message.syncEpoch}, ActiveEpoch={flow.Act1StartupBootstrapSyncEpoch}.");
                return;
            }

            StartupStepRecordResult result = flow.RecordPendingStartupStepCompletionMessage(
                message.syncEpoch,
                message.stepIndex,
                senderId,
                message.totalStepCount,
                message.modifierId,
                message.nextChoiceId);

            if (result != StartupStepRecordResult.Duplicate)
            {
                ModLog.Info(
                    $"Received CTA startup step completion from player {senderId}. " +
                    $"Epoch={message.syncEpoch}, Step={message.stepIndex + 1}/{message.totalStepCount}, " +
                    $"Modifier={message.modifierId}, NextChoiceId={message.nextChoiceId}, Result={result}, " +
                    $"Pending={flow.DescribePendingStartupStepCompletionMessages()}.");
                runStateForAlignment = runState;
                shouldAlign = true;
            }
        }

        if (shouldAlign && runStateForAlignment != null)
        {
            AlignChoiceIdBaselineForPlayer(
                runStateForAlignment,
                senderId,
                message.nextChoiceId,
                "received CTA startup completion");
        }
    }

    private static async Task SyncModifierBootstrapStepCompletionAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        IReadOnlyList<Player> orderedPlayers,
        Player localPlayer,
        int syncEpoch,
        int stepIndex,
        int totalStepCount,
        string modifierId)
    /*
     * Broadcasts or records a player's modifier bootstrap completion and waits until all expected players have reported.
     */
    {
        uint localNextChoiceId = GetNextChoiceIdForPlayer(runState, localPlayer);

        flow.RecordPendingStartupStepCompletionMessage(
            syncEpoch,
            stepIndex,
            localPlayer.NetId,
            totalStepCount,
            modifierId,
            localNextChoiceId);

        RunManager.Instance.NetService.SendMessage(ChooseTheAncientStartupStepCompletedMessage.Create(
            syncEpoch,
            stepIndex,
            totalStepCount,
            modifierId,
            localNextChoiceId));

        for (int frame = 0; frame < StartupBootstrapBarrierMaxFrames; frame++)
        {
            bool allPlayersReported = orderedPlayers.All(player =>
                flow.HasPendingStartupStepCompletionMessageForEpoch(syncEpoch, stepIndex, player.NetId));

            if (allPlayersReported)
            {
                IReadOnlyDictionary<ulong, StartupStepCompletionInfo> completions =
                    flow.GetPendingStartupStepCompletionMessagesForEpoch(syncEpoch, stepIndex);

                if (completions.Values.Select(info => info.TotalStepCount).Distinct().Count() > 1)
                {
                    ModLog.Warn(
                        $"CTA startup bootstrap step count mismatch at step {stepIndex + 1}. " +
                        $"Messages={flow.DescribePendingStartupStepCompletionMessages()}");
                }

                if (completions.Values.Select(info => info.ModifierId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                {
                    ModLog.Warn(
                        $"CTA startup bootstrap modifier mismatch at step {stepIndex + 1}. " +
                        $"Messages={flow.DescribePendingStartupStepCompletionMessages()}");
                }

                AlignChoiceIdBaselinesFromStartupStepMessages(runState, orderedPlayers, completions);
                await ChooseTheAncientHelpers.WaitForProcessFramesAsync(2);
                return;
            }

            await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);
        }

        ModLog.Warn(
            $"Timed out waiting for CTA startup bootstrap step completion barrier; applying available baselines and continuing to avoid a hard deadlock. " +
            $"Epoch={syncEpoch}, Step={stepIndex + 1}/{totalStepCount}, Modifier={modifierId}, " +
            $"Count={flow.GetPendingStartupStepCompletionMessageCountForEpoch(syncEpoch, stepIndex)}, " +
            $"Pending={flow.DescribePendingStartupStepCompletionMessages()}.");

        IReadOnlyDictionary<ulong, StartupStepCompletionInfo> availableCompletions =
            flow.GetPendingStartupStepCompletionMessagesForEpoch(syncEpoch, stepIndex);
        AlignChoiceIdBaselinesFromStartupStepMessages(runState, orderedPlayers, availableCompletions);
        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(2);
    }

    private static void AlignChoiceIdBaselinesFromStartupStepMessages(
        RunState runState,
        IReadOnlyList<Player> orderedPlayers,
        IReadOnlyDictionary<ulong, StartupStepCompletionInfo> completions)
    /*
     * Replays reserved startup-step choice IDs so later CTA choices stay aligned across host and clients.
     */
    {
        foreach (Player player in orderedPlayers)
        {
            if (!completions.TryGetValue(player.NetId, out StartupStepCompletionInfo? completion))
                continue;

            AlignChoiceIdBaselineForPlayer(
                runState,
                player.NetId,
                completion.NextChoiceId,
                "CTA startup completion barrier");
        }
    }

    private static void AlignChoiceIdBaselineForPlayer(
        RunState runState,
        ulong playerNetId,
        uint targetNextChoiceId,
        string reason)
    /*
     * Reserves missing choice IDs for one player until their local synchronizer baseline reaches the expected value.
     */
    {
        Player? player = runState.Players.FirstOrDefault(candidate => candidate.NetId == playerNetId);
        if (player == null)
        {
            ModLog.Warn(
                $"Could not align CTA startup choice id baseline for missing player {playerNetId}. " +
                $"TargetNextChoiceId={targetNextChoiceId}, Reason={reason}.");
            return;
        }

        uint currentNextChoiceId = GetNextChoiceIdForPlayer(runState, player);
        uint startingNextChoiceId = currentNextChoiceId;

        if (currentNextChoiceId > targetNextChoiceId)
        {
            ModLog.Debug(
                $"CTA startup choice id baseline for player {playerNetId} is already ahead of reported baseline. " +
                $"CurrentNextChoiceId={currentNextChoiceId}, ReportedNextChoiceId={targetNextChoiceId}, Reason={reason}.");
            return;
        }

        while (currentNextChoiceId < targetNextChoiceId)
        {
            RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
            currentNextChoiceId++;
        }

        if (startingNextChoiceId != currentNextChoiceId)
        {
            ModLog.Info(
                $"Aligned CTA startup choice id baseline for player {playerNetId}: " +
                $"{startingNextChoiceId}->{currentNextChoiceId}. Reason={reason}.");
        }
    }

    private static uint GetNextChoiceIdForPlayer(RunState runState, Player player)
    /*
     * Reads the next synchronized choice ID for a player from the PlayerChoiceSynchronizer slot list.
     */
    {
        int slotIndex = runState.GetPlayerSlotIndex(player);
        IReadOnlyList<uint> choiceIds = RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds;
        return slotIndex >= 0 && slotIndex < choiceIds.Count
            ? choiceIds[slotIndex]
            : 0u;
    }

    private static string GetModifierBootstrapId(ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    /*
     * Produces a stable identifier for a modifier bootstrap action for logging and deterministic ordering.
     */
    {
        string entry = bootstrapAction.Modifier.Id.Entry;
        return string.IsNullOrWhiteSpace(entry)
            ? bootstrapAction.Modifier.GetType().Name
            : entry;
    }

    private static int GetModifierBootstrapRunOrderIndex(ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    /*
     * Keeps the relative order produced by RunState.Modifiers. This avoids alphabetically having to reorder unrelated
     * modifier Neow options such as ALL_STAR, SPECIALIST, and third-party modifiers after they have been harvested.
     */
    {
        return bootstrapAction.RunModifierIndex;
    }

    private static List<ChooseTheAncientHelpers.ModifierBootstrapAction> OrderModifierBootstrapActions(
        IEnumerable<ChooseTheAncientHelpers.ModifierBootstrapAction> bootstrapActions)
    /*
     * Preserve RunState.Modifiers order for every recognized or unrecognized modifier.
     */
    {
        List<ChooseTheAncientHelpers.ModifierBootstrapAction> ordered = bootstrapActions
            .OrderBy(GetModifierBootstrapRunOrderIndex)
            .ToList();

        LogUnrecognizedModifierBootstrapActions(ordered);
        MoveModifierBeforeIfBothPresent(ordered, "SEALED_DECK", "DRAFT");

        return ordered;
    }

    private static void MoveModifierBeforeIfBothPresent(
        List<ChooseTheAncientHelpers.ModifierBootstrapAction> ordered,
        string modifierToMoveId,
        string targetModifierId)
    /*
     * Applies a single dependency without globally prioritizing known modifiers over unknown modifiers.
     */
    {
        int moverIndex = ordered.FindIndex(action =>
            string.Equals(GetModifierBootstrapId(action), modifierToMoveId, StringComparison.OrdinalIgnoreCase));
        int targetIndex = ordered.FindIndex(action =>
            string.Equals(GetModifierBootstrapId(action), targetModifierId, StringComparison.OrdinalIgnoreCase));

        if (moverIndex < 0 || targetIndex < 0 || moverIndex < targetIndex)
            return;

        ChooseTheAncientHelpers.ModifierBootstrapAction mover = ordered[moverIndex];
        ordered.RemoveAt(moverIndex);

        targetIndex = ordered.FindIndex(action =>
            string.Equals(GetModifierBootstrapId(action), targetModifierId, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            ordered.Add(mover);
            return;
        }

        ordered.Insert(targetIndex, mover);

        ModLog.Info(
            $"Adjusted modifier bootstrap order for dependency: {modifierToMoveId} before {targetModifierId}. " +
            $"Order={string.Join(", ", ordered.Select(action => $"{GetModifierBootstrapId(action)}@{GetModifierBootstrapRunOrderIndex(action)}"))}.");
    }

    private static void LogUnrecognizedModifierBootstrapActions(
        IReadOnlyList<ChooseTheAncientHelpers.ModifierBootstrapAction> ordered)
    /*
     * Unknown modifiers are supported through GenerateNeowOption. This log makes that compatibility path visible
     * without forcing CTA to special-case the modifier id.
     */
    {
        foreach (ChooseTheAncientHelpers.ModifierBootstrapAction action in ordered)
        {
            string modifierId = GetModifierBootstrapId(action);
            if (IsRecognizedModifierBootstrapId(modifierId))
                continue;

            ModLog.Info(
                $"Running unrecognized modifier bootstrap action {modifierId}@{GetModifierBootstrapRunOrderIndex(action)} " +
                "through the generic GenerateNeowOption path.");
        }
    }

    private static bool IsRecognizedModifierBootstrapId(string modifierId)
    /*
     * Known ids are used only for diagnostics and the tiny dependency rule above. They do not decide whether an action runs.
     */
    {
        return string.Equals(modifierId, "SEALED_DECK", StringComparison.OrdinalIgnoreCase)
               || string.Equals(modifierId, "DRAFT", StringComparison.OrdinalIgnoreCase)
               || string.Equals(modifierId, "SPECIALIZED", StringComparison.OrdinalIgnoreCase)
               || string.Equals(modifierId, "ALL_STAR", StringComparison.OrdinalIgnoreCase)
               || string.Equals(modifierId, "INSANITY", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<int>> CollectVotes(
        IReadOnlyList<Player> orderedPlayers,
        ChooseTheAncientSelectionScreen.RoundDefinition round,
        ChooseTheAncientSelectionScreen? localScreen)
    /*
     * Collects a vote for the supplied round from each player in slot order, using the local UI in singleplayer and synchronized choices in multiplayer.
     */
    {
        if (orderedPlayers.Count == 0)
            throw new InvalidOperationException("No players available for vote collection.");

        RunState? runState = orderedPlayers[0].RunState as RunState;
        if (runState != null && RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            if (localScreen == null)
                throw new InvalidOperationException("Local ancient selection screen was not created.");

            int localVote = await CollectSingleplayerVote(
                round,
                localScreen);

            localScreen.RecordVote(orderedPlayers[0], localVote);
            ModLog.Debug($"Vote received for player {orderedPlayers[0].NetId}: {localVote}");
            return [localVote];
        }

        Dictionary<ulong, uint> choiceIdsByPlayer = new();

        foreach (Player player in orderedPlayers)
        {
            uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
            choiceIdsByPlayer[player.NetId] = choiceId;
        }

        Task<int>[] voteTasks = orderedPlayers
            .Select(player => GetVoteForPlayer(
                player,
                choiceIdsByPlayer[player.NetId],
                round,
                localScreen))
            .ToArray();

        int[] votes = await Task.WhenAll(voteTasks);

        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            ModLog.Debug($"Vote received for player {orderedPlayers[i].NetId}: {votes[i]}");
        }

        return votes.ToList();
    }

    
    private static async Task<int> CollectSingleplayerVote(
        ChooseTheAncientSelectionScreen.RoundDefinition round,
        ChooseTheAncientSelectionScreen localScreen)
    /*
     * Runs the normal CTA screen and, when available, mirrors the same ballot into
     * Slay the Streamer through a reflection-only optional interop.
     */
    {
        // Lock before the round is built so there is no frame where a player can
        // click an option before the Twitch session takes ownership.
        localScreen.SetManualSelectionLocked(true);

        Task<int> screenVoteTask = localScreen.RunRoundAsync(round);
        await localScreen.WaitUntilRoundPresentedAsync();

        if (screenVoteTask.IsCompleted)
            return await screenVoteTask;

        string voteLabel = ChooseTheAncientBaseAncientText.GetStreamerVoteLabel(
            round.RoundType ==
            ChooseTheAncientSelectionScreen.VoteRoundType.FinalRevealVote);

        string[] optionLabels = round.Pool
            .Select((ancient, index) =>
            {
                string title = ancient.Title.GetFormattedText();
                if (!string.IsNullOrWhiteSpace(title))
                    return title;

                return string.IsNullOrWhiteSpace(ancient.Id.Entry)
                    ? ChooseTheAncientBaseAncientText.GetStreamerAncientFallbackLabel(index)
                    : ancient.Id.Entry;
            })
            .ToArray();

        if (!SlayTheStreamerInterop.TryStartVote(
                voteLabel,
                optionLabels,
                out SlayTheStreamerVote? streamerVote,
                out string unavailableReason)
            || streamerVote == null)
        {
            localScreen.SetManualSelectionLocked(false);
            ModLog.Debug(
                $"Slay the Streamer did not take this CTA ballot because {unavailableReason}. " +
                "Manual selection was restored.");
            return await screenVoteTask;
        }

        try
        {
            Task<int> firstCompleted = await Task.WhenAny(
                screenVoteTask,
                streamerVote.WinnerTask);

            if (ReferenceEquals(firstCompleted, screenVoteTask))
            {
                // Screen was Closed. Manual input is locked while this session is active.
                if (!screenVoteTask.IsCompletedSuccessfully)
                {
                    TryCancelSlayTheStreamerVote(streamerVote);
                    return await screenVoteTask;
                }

                int externallyResolvedVote = await screenVoteTask;
                streamerVote.TryForceWinner(externallyResolvedVote);
                return externallyResolvedVote;
            }

            int chatWinner = await streamerVote.WinnerTask;
            if (chatWinner < 0 || chatWinner >= round.Pool.Count)
            {
                ModLog.Warn(
                    $"Slay the Streamer returned invalid CTA option index {chatWinner}; " +
                    $"expected 0..{round.Pool.Count - 1}. Falling back to normal selection.");

                SetManualSelectionLockedDeferred(localScreen, locked: false);
                return await screenVoteTask;
            }

            Callable.From(() =>
            {
                if (!localScreen.TrySubmitExternalVote(chatWinner))
                {
                    ModLog.Debug(
                        $"CTA ignored chat option {chatWinner} because the screen " +
                        "had already resolved or was closing.");
                }
            }).CallDeferred();

            int resolvedVote = await screenVoteTask;

            if (resolvedVote != chatWinner)
            {
                ModLog.Debug(
                    $"CTA resolved to {resolvedVote} while chat resolved to {chatWinner}; " +
                    "the already-completed local screen choice was kept.");
            }
            else
            {
                ModLog.Info(
                    $"Slay the Streamer selected CTA option {chatWinner}: " +
                    $"{optionLabels[chatWinner]}.");
            }

            return resolvedVote;
        }
        catch (OperationCanceledException)
        {
            TryCancelSlayTheStreamerVote(streamerVote);

            if (screenVoteTask.IsCanceled)
                throw;

            SetManualSelectionLockedDeferred(localScreen, locked: false);
            ModLog.Warn(
                "The Slay the Streamer CTA vote was cancelled. " +
                "Manual selection was restored.");
            return await screenVoteTask;
        }
        catch (Exception ex)
        {
            TryCancelSlayTheStreamerVote(streamerVote);
            SetManualSelectionLockedDeferred(localScreen, locked: false);
            ModLog.Warn(
                "The Slay the Streamer CTA vote failed after opening. " +
                $"Manual selection was restored. {ex}");
            return await screenVoteTask;
        }
    }

    private static void SetManualSelectionLockedDeferred(
        ChooseTheAncientSelectionScreen screen,
        bool locked)
    {
        Callable.From(() => screen.SetManualSelectionLocked(locked)).CallDeferred();
    }

    private static void TryCancelSlayTheStreamerVote(SlayTheStreamerVote vote)
    {
        try
        {
            vote.Cancel();
        }
        catch (Exception ex)
        {
            ModLog.Debug(
                $"Could not cancel the reflected Slay the Streamer vote during cleanup: " +
                $"{ex.GetBaseException().Message}");
        }
    }

    private static async Task<int> GetVoteForPlayer(
        Player player,
        uint choiceId,
        ChooseTheAncientSelectionScreen.RoundDefinition round,
        ChooseTheAncientSelectionScreen? localScreen)
    /*
     * Runs the local selection UI or waits for a remote synchronized choice for a single player.
     */
    {
        RunState? runState = player.RunState as RunState;
        bool isSinglePlayer = runState != null && RunManager.Instance.NetService.Type == NetGameType.Singleplayer;

        if (isSinglePlayer || ShouldSelectLocally(player))
        {
            if (localScreen == null)
            {
                throw new InvalidOperationException("Local ancient selection screen was not created.");
            }

            int localVote = await localScreen.RunRoundAsync(round);

            localScreen.RecordVote(player, localVote);

            if (!isSinglePlayer)
            {
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    player,
                    choiceId,
                    PlayerChoiceResult.FromIndex(localVote));
            }

            return localVote;
        }

        int remoteVote = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(player, choiceId))
            .AsIndex();

        localScreen?.RecordVote(player, remoteVote);
        return remoteVote;
    }

    private static bool ShouldSelectLocally(Player player)
    /*
     * Determines whether this client is responsible for presenting the selection UI for the given player.
     */
    {
        if (LocalContext.IsMe(player))
        {
            return RunManager.Instance.NetService.Type != NetGameType.Replay;
        }

        return false;
    }

    public static List<T> BuildSecondRoundFinalistDisplayOrder<T>(
        IEnumerable<T>? finalists,
        Func<T, string>? getAncientId,
        Action<List<T>>? shuffle)
    /*
     * Builds the displayed order for the two second-round finalists using the same shape as the first-round
     * ballot shuffle: start from a stable ID-sorted order, then apply the supplied deterministic shuffle.
     * If the input is unexpectedly malformed, fail soft and keep the existing order rather than crashing the event.
     */
    {
        List<T> originalOrder = finalists?.ToList() ?? [];

        if (getAncientId == null)
        {
            ModLog.Warn("Second-round finalist display order could not be ID-sorted because getAncientId was null; keeping existing order.");
            return originalOrder;
        }

        if (shuffle == null)
        {
            ModLog.Warn("Second-round finalist display order could not be shuffled because shuffle was null; keeping existing order.");
            return originalOrder;
        }

        List<T> displayOrder = originalOrder
            .DistinctBy(getAncientId)
            .OrderBy(getAncientId, StringComparer.Ordinal)
            .ToList();

        if (displayOrder.Count != 2)
        {
            ModLog.Warn(
                $"Second-round finalist display order expected exactly two unique finalists, got {displayOrder.Count}; keeping existing order.");
            return originalOrder;
        }

        try
        {
            shuffle(displayOrder);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Second-round finalist display shuffle failed; keeping existing order. {ex}");
            return originalOrder;
        }

        return displayOrder;
    }

    public static string? ResolveSuppressedPreviewAncientIdForSecondRound(
        IReadOnlyList<string>? firstRoundPoolAncientIds,
        IReadOnlyList<string>? finalistAncientIds,
        IReadOnlyList<int>? firstVotes,
        string? firstRoundWinnerAncientId)
    /*
     * Resolves which finalist should keep its preview suppressed in the second round.
     * When the finalists are tied on first-round votes, reuse the already-resolved first-round winner so the
     * vote-resolution highlight and the suppressed preview ancient cannot disagree.
     * If data is unexpectedly malformed, fail soft and fall back to a finalist instead of crashing the event.
     */
    {
        List<string> uniqueFinalists = finalistAncientIds?
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (uniqueFinalists.Count == 0)
        {
            ModLog.Warn("Second-round preview suppression could not resolve a finalist because no finalist IDs were provided.");
            return firstRoundWinnerAncientId;
        }

        bool winnerIsFinalist = firstRoundWinnerAncientId != null
            && uniqueFinalists.Contains(firstRoundWinnerAncientId, StringComparer.Ordinal);

        if (uniqueFinalists.Count != 2)
        {
            ModLog.Warn(
                $"Second-round preview suppression expected exactly two unique finalists, got {uniqueFinalists.Count}; falling back to first available finalist.");
            return winnerIsFinalist ? firstRoundWinnerAncientId : uniqueFinalists[0];
        }

        if (!winnerIsFinalist)
        {
            ModLog.Warn(
                $"First-round winner {firstRoundWinnerAncientId ?? "<null>"} was not one of the second-round finalists; falling back to first available finalist.");
            return uniqueFinalists[0];
        }

        if (firstRoundPoolAncientIds == null || firstVotes == null)
        {
            ModLog.Warn("Second-round preview suppression could not count first-round votes; falling back to the first-round winner.");
            return firstRoundWinnerAncientId;
        }

        Dictionary<string, int> finalistVoteCounts = uniqueFinalists
            .ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        foreach (int vote in firstVotes)
        {
            if (vote < 0 || vote >= firstRoundPoolAncientIds.Count)
            {
                continue;
            }

            string votedAncientId = firstRoundPoolAncientIds[vote];
            if (finalistVoteCounts.ContainsKey(votedAncientId))
            {
                finalistVoteCounts[votedAncientId]++;
            }
        }

        int maxVotes = finalistVoteCounts.Values.Max();
        List<string> leaders = finalistVoteCounts
            .Where(kvp => kvp.Value == maxVotes)
            .Select(kvp => kvp.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return leaders.Count == 1
            ? leaders[0]
            : firstRoundWinnerAncientId;
    }


    private static (AncientEventModel? suppressedPreviewAncient, AncientEventModel? reactionAncient, string? suppressedPreviewAncientId, string? reactionAncientId) ResolveSecondRoundPresentation(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> firstRoundPool,
        IReadOnlyList<AncientEventModel> finalists,
        IReadOnlyList<int> firstVotes,
        string firstRoundWinnerAncientId)
    /*
     * Chooses which finalist has its reward preview hidden and which finalist reacts during the second round presentation.
     */
    {
        if (finalists.Count != 2)
        {
            return (null, null, null, null);
        }

        Dictionary<string, int> finalistVoteCounts = finalists
            .ToDictionary(ancient => ancient.Id.Entry, _ => 0);

        foreach (int vote in firstVotes)
        {
            if (vote < 0 || vote >= firstRoundPool.Count)
            {
                continue;
            }

            string votedAncientId = firstRoundPool[vote].Id.Entry;
            if (finalistVoteCounts.ContainsKey(votedAncientId))
            {
                finalistVoteCounts[votedAncientId]++;
            }
        }

        string? suppressedPreviewAncientId = ResolveSuppressedPreviewAncientIdForSecondRound(
            firstRoundPool.Select(ancient => ancient.Id.Entry).ToList(),
            finalists.Select(ancient => ancient.Id.Entry).ToList(),
            firstVotes,
            firstRoundWinnerAncientId);

        AncientEventModel suppressedPreviewAncient = finalists
            .FirstOrDefault(ancient => ancient.Id.Entry == suppressedPreviewAncientId)
            ?? finalists[0];

        int leftCount = finalistVoteCounts[finalists[0].Id.Entry];
        int rightCount = finalistVoteCounts[finalists[1].Id.Entry];

        AncientEventModel reactionAncient = finalists
            .FirstOrDefault(ancient => ancient.Id.Entry != suppressedPreviewAncient.Id.Entry)
            ?? finalists[1];

        ModLog.Debug($"Second vote presentation decided from round-one votes: suppress={suppressedPreviewAncient.Id.Entry}, reaction={reactionAncient.Id.Entry}, voteCounts={leftCount}/{rightCount}");
        // return SuppressedPreviewAncient to pass on to the selection screen
        return (suppressedPreviewAncient, reactionAncient, suppressedPreviewAncient.Id.Entry, reactionAncient.Id.Entry);
    }
    
    private static int ResolveSecondPlaceIndex(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> firstRoundPool,
        int firstPlaceIndex,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    /*
     * Resolves the runner-up from the first-round vote after removing the winning index.
     * Tied non-winners are shuffled with a dedicated deterministic RNG based on the run seed, displayed pool,
     * tied candidate IDs, first-place ancient, and vote signature, so the tie is not resolved by sorted screen index.
     */
    {
        int optionCount = firstRoundPool.Count;
        if (optionCount <= 1)
        {
            throw new InvalidOperationException("Cannot resolve second place from fewer than two options.");
        }

        if (firstPlaceIndex < 0 || firstPlaceIndex >= optionCount)
        {
            throw new InvalidOperationException(
                $"First-place index {firstPlaceIndex} is out of range for option count {optionCount}.");
        }

        Dictionary<int, int> nonWinnerCounts = Enumerable.Range(0, optionCount)
            .Where(index => index != firstPlaceIndex)
            .ToDictionary(index => index, _ => 0);

        foreach (int vote in votesInPlayerSlotOrder)
        {
            if (vote >= 0 && vote < optionCount && vote != firstPlaceIndex)
            {
                nonWinnerCounts[vote]++;
            }
        }

        int maxVotes = nonWinnerCounts.Values.Max();

        List<int> leaders = nonWinnerCounts
            .Where(kvp => kvp.Value == maxVotes)
            .Select(kvp => kvp.Key)
            .OrderBy(index => index)
            .ToList();

        if (leaders.Count == 1)
        {
            return leaders[0];
        }

        var rng = CreateSecondPlaceTieBreakRng(
            runState,
            nextActIndex,
            firstRoundPool,
            firstPlaceIndex,
            votesInPlayerSlotOrder,
            leaders);

        List<int> shuffledLeaders = leaders.ToList();
        rng.Shuffle(shuffledLeaders);
        int chosenLeader = shuffledLeaders[0];

        if (ModLog.IsDebugEnabled)
        {
            string countSummary = string.Join(
                ", ",
                nonWinnerCounts
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            string tiedLeaders = string.Join(
                ", ",
                leaders.Select(index => $"{index}:{firstRoundPool[index].Id.Entry}"));

            string shuffledLeaderOrder = string.Join(
                ", ",
                shuffledLeaders.Select(index => $"{index}:{firstRoundPool[index].Id.Entry}"));

            ModLog.Debug(
                $"Second-place tie for act {nextActIndex + 1} after excluding first-place index {firstPlaceIndex} " +
                $"({firstRoundPool[firstPlaceIndex].Id.Entry}). Counts={countSummary}; " +
                $"tied leaders=[{tiedLeaders}]; shuffled tie order=[{shuffledLeaderOrder}]; selected={chosenLeader}:{firstRoundPool[chosenLeader].Id.Entry}.");
        }

        return chosenLeader;
    }

    private static Rng CreateSecondPlaceTieBreakRng(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> firstRoundPool,
        int firstPlaceIndex,
        IReadOnlyList<int> votesInPlayerSlotOrder,
        IReadOnlyList<int> tiedLeaderIndices)
    /*
     * Creates the deterministic RNG used only for shuffling tied runner-up candidates in the first CTA round.
     * The seed name includes the displayed pool and tied candidate IDs so the tie-break is not just a stable
     * left-to-right index pick, while still remaining synchronized for multiplayer and replay paths.
     */
    {
        string firstAncientId = firstRoundPool[firstPlaceIndex].Id.Entry;
        string voteSignature = string.Join(",", votesInPlayerSlotOrder);
        string poolSignature = string.Join(
            "|",
            firstRoundPool.Select((ancient, index) => $"{index}:{ancient.Id.Entry}"));
        string tiedSignature = string.Join(
            "|",
            tiedLeaderIndices.Select(index => $"{index}:{firstRoundPool[index].Id.Entry}"));

        return ChooseTheAncientHelpers.CreateRunScopedRng(
            runState,
            "second_place_tie",
            "act",
            nextActIndex,
            "first",
            $"{firstPlaceIndex}:{firstAncientId}",
            "votes",
            voteSignature,
            "pool",
            poolSignature,
            "tied",
            tiedSignature);
    }

    private static int ResolveMostVotedIndex(
        RunState runState,
        int nextActIndex,
        int optionCount,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    /*
     * Resolves the winning option index from a vote list, using the final-vote RNG when multiple options tie for first.
     */
    {
        List<int> leaders = ResolveIndicesWithTargetCount(
            optionCount,
            votesInPlayerSlotOrder,
            selectMinimum: false);

        if (leaders.Count == 1)
        {
            return leaders[0];
        }

        var rng = ChooseTheAncientHelpers.CreateFinalVoteResolutionRng(runState, nextActIndex);
        return leaders[rng.NextInt(leaders.Count)];
    }

    private static List<int> ResolveIndicesWithTargetCount(
        int optionCount,
        IReadOnlyList<int> votesInPlayerSlotOrder,
        bool selectMinimum)
    /*
     * Counts valid votes by option index and returns every index matching either the highest or lowest vote count.
     */
    {
        if (optionCount <= 0)
        {
            throw new InvalidOperationException("Cannot resolve a vote for an empty option list.");
        }

        Dictionary<int, int> counts = Enumerable.Range(0, optionCount)
            .ToDictionary(index => index, _ => 0);

        foreach (int vote in votesInPlayerSlotOrder)
        {
            if (vote >= 0 && vote < optionCount)
            {
                counts[vote]++;
            }
        }

        int target = selectMinimum
            ? counts.Values.Min()
            : counts.Values.Max();

        return counts
            .Where(kvp => kvp.Value == target)
            .Select(kvp => kvp.Key)
            .OrderBy(index => index)
            .ToList();
    }
    
    private static Player GetHostPlayer(IReadOnlyList<Player> orderedPlayers)
    /*
     * Resolves the host player for configuration synchronization, falling back to slot zero when host metadata is unavailable.
     */
    {
        switch (RunManager.Instance.NetService.Type)
        {
            case NetGameType.Singleplayer:
            case NetGameType.Replay:
            case NetGameType.Host:
                return LocalContext.GetMe(orderedPlayers) ?? orderedPlayers[0];

            case NetGameType.Client:
                if (RunManager.Instance.NetService is INetClientGameService clientService &&
                    clientService.NetClient != null)
                {
                    ulong hostNetId = clientService.NetClient.HostNetId;
                    Player? hostPlayer = orderedPlayers.FirstOrDefault(p => p.NetId == hostNetId);
                    if (hostPlayer != null)
                        return hostPlayer;
                }

                break;
        }

        return orderedPlayers[0];
    }

    /// <summary>
    /// Mkes the host authoritative for the Selection Screen Ballot in mulitplayer. 
    /// </summary>
    private static async Task<List<AncientEventModel>> GetEffectiveBallotAsync(
        RunState runState,
        int targetActIndex,
        IReadOnlyList<Player> orderedPlayers,
        List<AncientEventModel> candidatePool,
        int ancientCount)
    /*
     * The host applies all local weighting/filtering, then broadcasts the selected ballot as
     * indices into a stable, ID-sorted copy of the pre-filter candidate pool. Every peer reserves
     * and consumes the same fixed number of host choices for the configured ballot size.
     */
    {
        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            return ChooseTheAncientHelpers.LimitCandidatePoolForVote(
                runState,
                targetActIndex,
                candidatePool,
                ancientCount);
        }

        List<AncientEventModel> canonicalPool = candidatePool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry, StringComparer.Ordinal)
            .ToList();

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        int syncedSlotCount = Math.Clamp(ancientCount, 0, 8);

        uint ballotCountChoiceId =
            RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        uint[] ballotSlotChoiceIds = new uint[syncedSlotCount];
        for (int slot = 0; slot < syncedSlotCount; slot++)
        {
            ballotSlotChoiceIds[slot] =
                RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);
        }

        if (LocalContext.IsMe(hostPlayer))
        {
            List<AncientEventModel> hostBallot =
                ChooseTheAncientHelpers.LimitCandidatePoolForVote(
                    runState,
                    targetActIndex,
                    candidatePool,
                    ancientCount);

            if (hostBallot.Count > syncedSlotCount)
            {
                throw new InvalidOperationException(
                    $"Host CTA ballot for act {targetActIndex + 1} contained {hostBallot.Count} candidates, " +
                    $"but only {syncedSlotCount} synchronized slots were reserved.");
            }

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                ballotCountChoiceId,
                PlayerChoiceResult.FromIndex(hostBallot.Count));

            List<int> canonicalIndices = new(hostBallot.Count);
            for (int slot = 0; slot < syncedSlotCount; slot++)
            {
                int canonicalIndex = 0;

                if (slot < hostBallot.Count)
                {
                    AncientEventModel selectedAncient = hostBallot[slot];
                    canonicalIndex = canonicalPool.FindIndex(ancient =>
                        string.Equals(ancient.Id.Entry, selectedAncient.Id.Entry, StringComparison.Ordinal));

                    if (canonicalIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Host CTA ballot ancient {selectedAncient.Id.Entry} was not present in the canonical " +
                            $"candidate pool for act {targetActIndex + 1}.");
                    }

                    canonicalIndices.Add(canonicalIndex);
                }

                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    hostPlayer,
                    ballotSlotChoiceIds[slot],
                    PlayerChoiceResult.FromIndex(canonicalIndex));
            }

            ModLog.Info(
                $"Broadcasting host-authoritative CTA ballot for act {targetActIndex + 1}: " +
                $"count={hostBallot.Count}, canonicalIndices={string.Join(",", canonicalIndices)}, " +
                $"ancients={ChooseTheAncientHelpers.DescribeAncients(hostBallot)}");

            return hostBallot;
        }

        int syncedBallotCount = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, ballotCountChoiceId))
            .AsIndex();

        int[] syncedCanonicalIndices = new int[syncedSlotCount];
        for (int slot = 0; slot < syncedSlotCount; slot++)
        {
            syncedCanonicalIndices[slot] = (await RunManager.Instance.PlayerChoiceSynchronizer
                    .WaitForRemoteChoice(hostPlayer, ballotSlotChoiceIds[slot]))
                .AsIndex();
        }

        if (syncedBallotCount < 0 || syncedBallotCount > syncedSlotCount)
        {
            throw new InvalidOperationException(
                $"Received invalid host CTA ballot count {syncedBallotCount} for act {targetActIndex + 1}; " +
                $"reserved slots={syncedSlotCount}.");
        }

        List<AncientEventModel> syncedBallot = new(syncedBallotCount);
        HashSet<string> selectedIds = new(StringComparer.Ordinal);

        for (int slot = 0; slot < syncedBallotCount; slot++)
        {
            int canonicalIndex = syncedCanonicalIndices[slot];
            if (canonicalIndex < 0 || canonicalIndex >= canonicalPool.Count)
            {
                throw new InvalidOperationException(
                    $"Received invalid host CTA ballot index {canonicalIndex} at slot {slot} " +
                    $"for act {targetActIndex + 1}; canonical pool count={canonicalPool.Count}.");
            }

            AncientEventModel selectedAncient = canonicalPool[canonicalIndex];
            if (!selectedIds.Add(selectedAncient.Id.Entry))
            {
                throw new InvalidOperationException(
                    $"Received duplicate host CTA ballot ancient {selectedAncient.Id.Entry} " +
                    $"for act {targetActIndex + 1}.");
            }

            syncedBallot.Add(selectedAncient);
        }

        ModLog.Info(
            $"Received host-authoritative CTA ballot for act {targetActIndex + 1}: " +
            $"count={syncedBallot.Count}, canonicalIndices={string.Join(",", syncedCanonicalIndices.Take(syncedBallotCount))}, " +
            $"ancients={ChooseTheAncientHelpers.DescribeAncients(syncedBallot)}");

        return syncedBallot;
    }


    private static async Task<bool> GetEffectiveEnableRedundantSettingsAsync(
        IReadOnlyList<Player> orderedPlayers)
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
            return ChooseTheAncientConfig.EnableRedundantSettings;

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int encodedEnabled = ChooseTheAncientConfig.EnableRedundantSettings ? 1 : 0;
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(encodedEnabled));

            ModLog.Debug(
                $"Broadcasting host redundant-settings gate: enabled={ChooseTheAncientConfig.EnableRedundantSettings}.");
            return ChooseTheAncientConfig.EnableRedundantSettings;
        }

        int syncedEnabled = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        bool enabled = syncedEnabled != 0;
        ModLog.Debug($"Received host redundant-settings gate: enabled={enabled}.");
        return enabled;
    }


    private static async Task<IReadOnlyList<int>?> GetEffectiveAncientPoolSourceActsAsync(
        IReadOnlyList<Player> orderedPlayers,
        int targetActIndex)
    /*
     * Collects and resolves the active ancient source-act filter across players before building the CTA ballot.
     */
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        if (!ChooseTheAncientConfig.HasAncientPoolSourceActConfig(targetActIndex))
        {
            ModLog.Warn($"GetEffectiveAncientPoolSourceActsAsync found no source-act configuration row for act {targetActIndex + 1}.");
            return null;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            IReadOnlyList<int> localEnabledSourceActs =
                ChooseTheAncientConfig.GetEnabledAncientPoolSourceActs(targetActIndex);
            ModLog.Debug(
                $"Using local ancient pool source acts for act {targetActIndex + 1}: " +
                $"{ChooseTheAncientConfig.DescribeAncientPoolSourceActs(localEnabledSourceActs)}");
            return localEnabledSourceActs;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int hostSourceActMask = ChooseTheAncientConfig.GetAncientPoolSourceActMask(targetActIndex);
            IReadOnlyList<int> hostEnabledSourceActs =
                ChooseTheAncientConfig.GetEnabledAncientPoolSourceActsFromMask(targetActIndex, hostSourceActMask);

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(hostSourceActMask));

            ModLog.Debug(
                $"Broadcasting host ancient pool source acts for act {targetActIndex + 1}: " +
                $"mask={hostSourceActMask}, " +
                $"{ChooseTheAncientConfig.DescribeAncientPoolSourceActs(hostEnabledSourceActs)}");

            return hostEnabledSourceActs;
        }

        int syncedSourceActMask = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        IReadOnlyList<int> syncedEnabledSourceActs =
            ChooseTheAncientConfig.GetEnabledAncientPoolSourceActsFromMask(targetActIndex, syncedSourceActMask);

        ModLog.Debug(
            $"Received host ancient pool source acts for act {targetActIndex + 1}: " +
            $"mask={syncedSourceActMask}, " +
            $"{ChooseTheAncientConfig.DescribeAncientPoolSourceActs(syncedEnabledSourceActs)}");

        return syncedEnabledSourceActs;
    }


    private static async Task<IReadOnlyDictionary<string, bool>> GetEffectiveSpecialAncientOverridesAsync(
        IReadOnlyList<Player> orderedPlayers,
        int targetActIndex)
    /*
     * Collects and resolves per-player special ancient override toggles such as Neow and Darv.
     */
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            IReadOnlyDictionary<string, bool> localOverrides =
                ChooseTheAncientConfig.GetSpecialAncientOverridesSnapshot(targetActIndex);

            ModLog.Debug(
                $"Using local special ancient overrides for act {targetActIndex + 1}: " +
                $"{ChooseTheAncientConfig.DescribeSpecialAncientOverrides(localOverrides)}");

            return localOverrides;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int hostSpecialOverrideMask = ChooseTheAncientConfig.GetSpecialAncientOverrideMask(targetActIndex);
            IReadOnlyDictionary<string, bool> hostOverrides =
                ChooseTheAncientConfig.GetSpecialAncientOverridesFromMask(targetActIndex, hostSpecialOverrideMask);

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(hostSpecialOverrideMask));

            ModLog.Debug(
                $"Broadcasting host special ancient overrides for act {targetActIndex + 1}: " +
                $"mask={hostSpecialOverrideMask}, " +
                $"{ChooseTheAncientConfig.DescribeSpecialAncientOverrides(hostOverrides)}");

            return hostOverrides;
        }

        int syncedSpecialOverrideMask = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        IReadOnlyDictionary<string, bool> syncedOverrides =
            ChooseTheAncientConfig.GetSpecialAncientOverridesFromMask(targetActIndex, syncedSpecialOverrideMask);

        ModLog.Debug(
            $"Received host special ancient overrides for act {targetActIndex + 1}: " +
            $"mask={syncedSpecialOverrideMask}, " +
            $"{ChooseTheAncientConfig.DescribeSpecialAncientOverrides(syncedOverrides)}");

        return syncedOverrides;
    }

    private static async Task<ChooseTheAncientConfig.SelectionGameMode> GetEffectiveGameModeAsync(
        IReadOnlyList<Player> orderedPlayers)
    /*
     * Collects and resolves the effective CTA game mode across players.
     */
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            return ChooseTheAncientConfig.GameMode;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int hostGameMode = (int)ChooseTheAncientConfig.GameMode;

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(hostGameMode));

            ModLog.Debug($"Broadcasting host GameMode={ChooseTheAncientConfig.GameMode}");
            return ChooseTheAncientConfig.GameMode;
        }

        int syncedMode = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        ChooseTheAncientConfig.SelectionGameMode normalizedMode =
            ChooseTheAncientConfig.NormalizeSelectionGameMode(syncedMode);

        ModLog.Debug($"Received host GameMode={normalizedMode}");
        return normalizedMode;
    }

    private static async Task<int> GetEffectiveAncientCountAsync(IReadOnlyList<Player> orderedPlayers)
    /*
     * Collects and resolves how many ancients should appear on the CTA ballot.
     */
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            ModLog.Debug($"Using local AncientCount={ChooseTheAncientConfig.AncientCount}");
            return ChooseTheAncientConfig.AncientCount;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int hostAncientCount = ChooseTheAncientConfig.AncientCount;

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(hostAncientCount));

            ModLog.Debug($"Broadcasting host AncientCount={hostAncientCount}");
            return hostAncientCount;
        }

        int syncedCount = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        syncedCount = Math.Clamp(syncedCount, 2, 8);

        ModLog.Debug($"Received host AncientCount={syncedCount}");
        return syncedCount;
    }
}
