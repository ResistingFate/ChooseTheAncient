using System;
using System.Collections.Generic;
using System.Linq;
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
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class ChooseTheAncientCoordinator
{

    private static readonly System.Reflection.MethodInfo ClearScreensMethod =
        AccessTools.Method(typeof(RunManager), "ClearScreens")
        ?? throw new InvalidOperationException("Could not locate RunManager.ClearScreens.");

    private static readonly System.Reflection.MethodInfo ExitCurrentRoomsMethod =
        AccessTools.Method(typeof(RunManager), "ExitCurrentRooms")
        ?? throw new InvalidOperationException("Could not locate RunManager.ExitCurrentRooms.");

    private static readonly System.Reflection.MethodInfo FadeInMethod =
        AccessTools.Method(typeof(RunManager), "FadeIn")
        ?? throw new InvalidOperationException("Could not locate RunManager.FadeIn.");

    private static async Task PrepareAct1SelectionUiAsync(RunManager runManager)
    {
        ModLog.Info("Preparing Act 1 selection UI by mirroring RunManager.EnterAct screen cleanup.");

        ClearScreensMethod.Invoke(runManager, null);

        object? exitRoomsTask = ExitCurrentRoomsMethod.Invoke(runManager, null);
        if (exitRoomsTask is Task task)
        {
            await task;
        }

        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);
    }






    private static async Task EnsureAct1StartupModifierBootstrapBeforeSelectionAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        IReadOnlyList<Player> orderedPlayers,
        string reason)
    {
        if (flow.Act1StartupBootstrapApplied)
        {
            ModLog.Debug(
                $"Skipping startup modifier bootstrap because it already completed earlier in this run. Reason={reason}");
            return;
        }

        Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
        if (!ChooseTheAncientHelpers.HasModifierBootstrapActions(localPlayer))
        {
            flow.Act1StartupBootstrapApplied = true;
            ModLog.Debug(
                $"No startup modifier bootstrap actions were present for the current Act 1 flow. Reason={reason}");
            return;
        }

        ModLog.Info(
            "Start-of-run modifier bootstrap was detected for the Act 1 starting-room flow. " +
            "Running it once before opening ChooseTheAncient so startup UI/state can complete first.");

        int startupBootstrapSyncEpoch = flow.BeginAct1StartupBootstrapSyncEpoch();
        flow.ClearPendingStartupStepCompletionMessages();


        await RunModifierBootstrapAsync(runState, flow, orderedPlayers, startupBootstrapSyncEpoch, reason);
        flow.Act1StartupBootstrapApplied = true;

        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "BootstrapCompleted",
            runState,
            flow,
            orderedPlayers,
            $"Reason={reason}");

        ChooseTheAncientHelpers.PurgeReceivedPlayerChoicesBeforeCurrentChoiceIds(
            flow,
            orderedPlayers,
            $"after {reason} and before CTA host-config sync");

        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "StartupChoicesPurged",
            runState,
            flow,
            orderedPlayers,
            $"Reason={reason}");

        ChooseTheAncientHelpers.FinalizeStartupFlowChoiceBaselinesFromCurrentState(
            runState,
            orderedPlayers,
            flow,
            $"after {reason} and before final CTA startup-step sync baseline alignment");

        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "StartupChoiceBaselinesFinalized",
            runState,
            flow,
            orderedPlayers,
            $"Reason={reason}");

        ChooseTheAncientHelpers.AlignPlayerChoiceIdsToStartupFlowBaselines(
            flow,
            orderedPlayers,
            $"after explicit CTA startup-step sync ({reason})");

        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "SharedSelectionFlowReady",
            runState,
            flow,
            orderedPlayers,
            $"Reason={reason}");

        ModLog.Info(
            $"Startup modifier bootstrap is now marked complete for this run and CTA may safely enter the shared selection flow. Reason={reason}");

    }

    private static uint ReserveChoiceIdForCtaFlow(
    ChooseTheAncientFlowState? flow,
    Player player)
{
    uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
    ModLog.Debug(
        $"Reserved CTA flow choice id {choiceId} for player {player.NetId}.");
    return choiceId;
}

public static async Task RunAct1StartingRoomFlowAsync(
    RunState runState,
    ChooseTheAncientFlowState flow)
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

        ChooseTheAncientConfig.RefreshFromModConfig();

        List<Player> orderedPlayers = runState.Players
            .OrderBy(runState.GetPlayerSlotIndex)
            .ToList();

        ChooseTheAncientHelpers.EnsureStartupSyncMessageHandlerRegistered(
            runState,
            flow,
            orderedPlayers,
            "Act 1 starting-room flow entered the CTA shell room");

        await EnsureAct1StartupModifierBootstrapBeforeSelectionAsync(
            runState,
            flow,
            orderedPlayers,
            "pre-selection startup modifier bootstrap");

        int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers, flow);
        ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers, flow);
        IReadOnlyList<int>? effectiveAncientPoolSourceActs =
            await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, targetActIndex: 0, flow);
        IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides =
            await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, targetActIndex: 0, flow);

        ModLog.Info(
            "Act 1 starting-room flow effective settings: " +
            $"AncientCount={ancientCount}, GameMode={gameMode}, " +
            $"SourceActs={ChooseTheAncientConfig.DescribeAncientPoolSourceActs(effectiveAncientPoolSourceActs ?? Array.Empty<int>())}, " +
            $"SpecialAncients={ChooseTheAncientConfig.DescribeSpecialAncientOverrides(effectiveSpecialAncientOverrides)}.");

        ActModel firstAct = runState.Acts[0];
        List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
            firstAct,
            runState,
            targetActIndex: 0,
            enabledSourceActsOverride: effectiveAncientPoolSourceActs,
            specialAncientOverridesOverride: effectiveSpecialAncientOverrides);

        if (flow.ModifierBootstrapCompleted)
        {
            pool = ChooseTheAncientHelpers.PreferNonNeowAncientsForActOne(pool);
        }

        pool = ChooseTheAncientHelpers.LimitCandidatePoolForVote(runState, 0, pool, ancientCount);

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

            ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
                "OpeningSelectionScreen",
                runState,
                flow,
                orderedPlayers,
                $"Pool={string.Join(", ", pool.Select(ancient => ancient.Id.Entry))}, GameMode={gameMode}");

            (chosen, localScreen) = await RunAncientSelectionBallotAsync(
                runState,
                0,
                orderedPlayers,
                pool,
                gameMode,
                flow);
        }

        ChooseTheAncientHelpers.SetChosenAncient(firstAct, chosen);
        ModLog.Info($"Chosen starting ancient for Act 1 from starting-room flow: {chosen.Id.Entry}");

        if (flow.Act1StartupBootstrapApplied)
        {
            ModLog.Info(
                "Act 1 startup modifier bootstrap was already consumed before CTA selection. " +
                "CTA will not run startup modifier bootstrap again after the ancient choice resolves.");
        }

        flow.ResolvedActs.Add(0);

        ChooseTheAncientHelpers.ConvertAct1StartShellToChosenAncient(runState, chosen);

        localScreen?.CloseScreen();
        localScreen = null;

        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "DispatchingChosenAncientTransition",
            runState,
            flow,
            orderedPlayers,
            $"ChosenAncient={chosen.Id.Entry}");

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
        ChooseTheAncientHelpers.ReleaseStartupSyncMessageHandlerContext(
            runState,
            flow,
            "Act 1 starting-room flow cleanup");
        flow.ClearStartupFlowChoiceIds();
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

            ClearScreensMethod.Invoke(runManager, null);
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

        ClearScreensMethod.Invoke(runManager, null);
        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

        ModLog.Info($"Entering Act 1 chosen ancient room for {chosenAncient.Id.Entry}.");
        await runManager.EnterRoom(new EventRoom(chosenAncient));

        object? fadeInTask = FadeInMethod.Invoke(runManager, new object?[] { true });
        if (fadeInTask is Task task)
        {
            await task;
        }

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

public static async Task RunAct1BeforeGenerateMapFlowAsync(
    RunManager runManager,
    RunState runState,
    ChooseTheAncientFlowState flow)
{
    ChooseTheAncientSelectionScreen? localScreen = null;

    try
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        List<Player> orderedPlayers = runState.Players
            .OrderBy(runState.GetPlayerSlotIndex)
            .ToList();

        ChooseTheAncientHelpers.EnsureStartupSyncMessageHandlerRegistered(
            runState,
            flow,
            orderedPlayers,
            "Act 1 starting-room flow entered the CTA shell room");

        await EnsureAct1StartupModifierBootstrapBeforeSelectionAsync(
            runState,
            flow,
            orderedPlayers,
            "pre-selection startup modifier bootstrap");

        int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);
        ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers);
        IReadOnlyList<int>? effectiveAncientPoolSourceActs =
            await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, targetActIndex: 0);

        IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides =
            await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, targetActIndex: 0);

        ActModel firstAct = runState.Acts[0];
        List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
            firstAct,
            runState,
            targetActIndex: 0,
            enabledSourceActsOverride: effectiveAncientPoolSourceActs,
            specialAncientOverridesOverride: effectiveSpecialAncientOverrides);

        if (flow.ModifierBootstrapCompleted)
        {
            pool = ChooseTheAncientHelpers.PreferNonNeowAncientsForActOne(pool);
        }

        if (ModLog.IsDebugEnabled)
        {
            string ancientPool = string.Join(",", pool.Select(ancient => ancient.Id.Entry));
            ModLog.Debug($"Available starting ancients to draw {ancientCount} from: {ancientPool}");
        }

        pool = ChooseTheAncientHelpers.LimitCandidatePoolForVote(runState, 0, pool, ancientCount);

        ChooseTheAncientHelpers.LogPool("Act 1 initial ballot", pool);
        ModLog.Info($"Using game mode {gameMode} for act 1.");

        if (pool.Count == 0)
        {
            ModLog.Warn("Act 1 starting ancient ballot is empty after filtering. Falling back to vanilla map generation.");
            flow.ResolvedActs.Add(0);
            flow.FlowInProgress = false;
            await runManager.GenerateMap();
            return;
        }

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
                ModLog.Info("Warming Act 1 ancient ballot visuals before opening the starting selection screen.");
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
        ChooseTheAncientHelpers.ForceAct1AncientStart(runState);
        ModLog.Info($"Chosen starting ancient for Act 1: {chosen.Id.Entry}");

        if (flow.Act1StartupBootstrapApplied)
        {
            ModLog.Info(
                "Act 1 startup modifier bootstrap was already consumed before CTA selection. " +
                "CTA will not run startup modifier bootstrap again after the ancient choice resolves.");
        }

        flow.ResolvedActs.Add(0);
        flow.FlowInProgress = false;
        await runManager.GenerateMap();
    }
    catch (OperationCanceledException ex)
    {
        ModLog.Warn(
            $"Act 1 starting ancient flow canceled at GenerateMap seam: {ex.GetType().Name}. " +
            "Falling back to vanilla map generation.");

        flow.ResolvedActs.Add(0);
        flow.FlowInProgress = false;
        await runManager.GenerateMap();
    }
    catch (Exception ex)
    {
        ModLog.Error($"Act 1 ancient selection flow at GenerateMap seam failed: {ex}");
        flow.ResolvedActs.Add(0);
        flow.FlowInProgress = false;
        await runManager.GenerateMap();
    }
    finally
    {
        localScreen?.CloseScreen();
        flow.FlowInProgress = false;
        ModLog.Info(
            $"Act 1 GenerateMap flow cleanup. " +
            $"InProgress={flow.FlowInProgress}, ModifierBootstrapCompleted={flow.ModifierBootstrapCompleted}");
    }
}

    public static async Task RunAsync(
        RunManager runManager,
        RunState runState,
        int nextActIndex,
        ChooseTheAncientFlowState flow)
    {
        ChooseTheAncientSelectionScreen? localScreen = null;

        try
        {
            List<Player> orderedPlayers = runState.Players
                .OrderBy(runState.GetPlayerSlotIndex)
                .ToList();

            int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);
            ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers);
            IReadOnlyList<int>? effectiveAncientPoolSourceActs =
                await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, nextActIndex);
            IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides =
                await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, nextActIndex);

            ActModel nextAct = runState.Acts[nextActIndex];
            List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
                nextAct,
                runState,
                nextActIndex,
                effectiveAncientPoolSourceActs,
                effectiveSpecialAncientOverrides);

            if (ModLog.IsDebugEnabled)
            {
                string ancientPool = string.Join(",", pool.Select(ancient => ancient.Id.Entry));
                ModLog.Debug($"Available ancients to draw {ancientCount} from for act {nextActIndex + 1}: {ancientPool}");
            }

            pool = ChooseTheAncientHelpers.LimitCandidatePoolForVote(runState, nextActIndex, pool, ancientCount);
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
                ModLog.Info($"Only one ancient available for act {nextActIndex + 1}: {chosen.Id.Entry}");
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
                ModLog.Info($"Chosen ancient for act {nextActIndex + 1}: {chosen.Id.Entry}");
            }

            flow.ResolvedActs.Add(nextActIndex);
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        catch (OperationCanceledException ex)
        {
            ModLog.Warn(
                $"Ancient selection flow canceled for act {nextActIndex + 1}: " +
                $"{ex.GetType().Name}. Skipping forced act progression.");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Ancient selection flow failed: {ex}");
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        finally
        {
            localScreen?.CloseScreen();
            flow.FlowInProgress = false;
            flow.ContinueEnterNextAct = false;
            ModLog.Info($"Ancient flow cleanup. InProgress={flow.FlowInProgress}, ContinueNext={flow.ContinueEnterNextAct}");
        }
    }

    
public static async Task RunAct1MapEntryFlowAsync(
    RunManager runManager,
    RunState runState,
    MapCoord startingCoord,
    ChooseTheAncientFlowState flow)
{
    ChooseTheAncientSelectionScreen? localScreen = null;

    try
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        List<Player> orderedPlayers = runState.Players
            .OrderBy(runState.GetPlayerSlotIndex)
            .ToList();

        ChooseTheAncientHelpers.EnsureStartupSyncMessageHandlerRegistered(
            runState,
            flow,
            orderedPlayers,
            "Act 1 starting-room flow entered the CTA shell room");

        await EnsureAct1StartupModifierBootstrapBeforeSelectionAsync(
            runState,
            flow,
            orderedPlayers,
            "pre-selection startup modifier bootstrap");

        int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);
        ChooseTheAncientConfig.SelectionGameMode gameMode = await GetEffectiveGameModeAsync(orderedPlayers);
        IReadOnlyList<int>? effectiveAncientPoolSourceActs =
            await GetEffectiveAncientPoolSourceActsAsync(orderedPlayers, targetActIndex: 0);

        IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides =
            await GetEffectiveSpecialAncientOverridesAsync(orderedPlayers, targetActIndex: 0);

        ActModel firstAct = runState.Acts[0];
        List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(
            firstAct,
            runState,
            targetActIndex: 0,
            enabledSourceActsOverride: effectiveAncientPoolSourceActs,
            specialAncientOverridesOverride: effectiveSpecialAncientOverrides);

        if (flow.ModifierBootstrapCompleted)
        {
            pool = ChooseTheAncientHelpers.PreferNonNeowAncientsForActOne(pool);
        }

        if (ModLog.IsDebugEnabled)
        {
            string ancientPool = string.Join(",", pool.Select(ancient => ancient.Id.Entry));
            ModLog.Debug($"Available starting ancients to draw {ancientCount} from: {ancientPool}");
        }

        pool = ChooseTheAncientHelpers.LimitCandidatePoolForVote(runState, 0, pool, ancientCount);

        ChooseTheAncientHelpers.LogPool("Act 1 starting ancient ballot", pool);
        ModLog.Info("Using game mode " + gameMode + " for the Act 1 starting ancient selection.");

        if (pool.Count == 0)
        {
            ModLog.Warn("Act 1 starting ancient ballot is empty after filtering. Falling back to vanilla starting ancient.");
            flow.ResolvedActs.Add(0);
            flow.ContinueEnterMapCoord = true;
            await runManager.EnterMapCoord(startingCoord);
            return;
        }

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
                await PrepareAct1SelectionUiAsync(runManager);
                ModLog.Info("Warming Act 1 ancient ballot visuals before opening the starting selection screen.");
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
        ChooseTheAncientHelpers.ForceAct1AncientStart(runState);

        ModLog.Info($"Chosen starting ancient for Act 1: {chosen.Id.Entry}");

        flow.ResolvedActs.Add(0);
        flow.ContinueEnterMapCoord = true;
        await runManager.EnterMapCoord(startingCoord);
    }
    catch (OperationCanceledException ex)
    {
        ModLog.Warn(
            $"Act 1 starting ancient flow canceled: {ex.GetType().Name}. " +
            "Falling back to vanilla starting ancient.");

        flow.ResolvedActs.Add(0);
        flow.ContinueEnterMapCoord = true;
        await runManager.EnterMapCoord(startingCoord);
    }
    catch (Exception ex)
    {
        ModLog.Error($"Act 1 starting ancient flow failed: {ex}");
        flow.ResolvedActs.Add(0);
        flow.ContinueEnterMapCoord = true;
        await runManager.EnterMapCoord(startingCoord);
    }
    finally
    {
        localScreen?.CloseScreen();
        flow.FlowInProgress = false;
        flow.ContinueEnterMapCoord = false;
        ModLog.Info(
            $"Act 1 ancient flow cleanup. " +
            $"InProgress={flow.FlowInProgress}, ContinueMapCoord={flow.ContinueEnterMapCoord}, ModifierBootstrapCompleted={flow.ModifierBootstrapCompleted}");
    }
}

private static async Task<(AncientEventModel Chosen, ChooseTheAncientSelectionScreen? LocalScreen)> RunAncientSelectionBallotAsync(
        RunState runState,
        int targetActIndex,
        IReadOnlyList<Player> orderedPlayers,
        List<AncientEventModel> pool,
        ChooseTheAncientConfig.SelectionGameMode gameMode,
        ChooseTheAncientFlowState? flow = null)
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
                localScreen,
                flow);

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
                localScreen,
                flow);

            int firstPlaceIndex = ResolveMostVotedIndex(
                runState,
                targetActIndex,
                pool.Count,
                firstVotes);

            int secondPlaceIndex = ResolveSecondPlaceIndex(
                runState,
                targetActIndex,
                pool.Count,
                firstPlaceIndex,
                firstVotes);

            AncientEventModel firstAncient = pool[firstPlaceIndex];
            AncientEventModel secondAncient = pool[secondPlaceIndex];

            if (localScreen != null)
            {
                await localScreen.PlayInitialVoteResolutionAsync(firstVotes, firstPlaceIndex);
            }

            finalists = [firstAncient, secondAncient];

            ModLog.Info($"First-pass elimination kept {firstAncient.Id.Entry}, {secondAncient.Id.Entry}.");
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
                firstVotes);

            if (gameMode == ChooseTheAncientConfig.SelectionGameMode.FairFight)
            {
                suppressedPreviewAncient = null;
                suppressedPreviewAncientId = null;
            }

            var secondRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                finalists,
                ChooseTheAncientSelectionScreen.VoteRoundType.FinalRevealVote,
                localPreviewData,
                suppressedPreviewAncientId,
                suppressedPreviewAncient,
                reactionAncientId,
                reactionAncient);

            List<int> finalVotes = await CollectVotes(
                orderedPlayers,
                secondRound,
                localScreen,
                flow);

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
        int startupBootstrapSyncEpoch,
        string reason)
    {
        Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
        if (localPlayer != null)
        {
            HashSet<string> executedBootstrapKeys = new(StringComparer.OrdinalIgnoreCase);
            int maxBootstrapPasses = Math.Max(8, runState.Modifiers.Count * 3);
            int consecutiveEmptyPasses = 0;

            for (int pass = 1; pass <= maxBootstrapPasses && consecutiveEmptyPasses < 2; pass++)
            {
                IReadOnlyList<ChooseTheAncientHelpers.ModifierBootstrapAction> discoveredActions =
                    ChooseTheAncientHelpers.BuildModifierBootstrapActions(localPlayer);

                List<(ChooseTheAncientHelpers.ModifierBootstrapAction Action, string Key)> pendingActions =
                    discoveredActions
                        .Select(action => (Action: action, Key: GetModifierBootstrapKey(runState, action)))
                        .Where(entry => !executedBootstrapKeys.Contains(entry.Key))
                        .OrderBy(entry => GetModifierBootstrapPriority(entry.Action))
                        .ThenBy(entry => GetModifierBootstrapId(entry.Action), StringComparer.Ordinal)
                        .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                        .ToList();

                if (pendingActions.Count == 0)
                {
                    consecutiveEmptyPasses++;

                    if (pass == 1)
                    {
                        ModLog.Info($"No start-of-run modifier bootstrap actions were found for local player {localPlayer.NetId}.");
                    }
                    else
                    {
                        ModLog.Debug(
                            $"No newly surfaced start-of-run modifier bootstrap actions were found on pass {pass} " +
                            $"for local player {localPlayer.NetId}. Reason={reason}. EmptyPasses={consecutiveEmptyPasses}/2.");
                    }

                    await ChooseTheAncientHelpers.WaitForStartupModifierStateToSettleAsync(
                        runState,
                        orderedPlayers,
                        $"{reason} (bootstrap probe pass {pass})",
                        requiredStableFrames: 3,
                        maxFrames: 180);

                    continue;
                }

                consecutiveEmptyPasses = 0;

                ModLog.Info(
                    $"Running {pendingActions.Count} newly surfaced start-of-run modifier bootstrap action(s) " +
                    $"for local player {localPlayer.NetId}. Reason={reason}. Pass={pass}/{maxBootstrapPasses}. " +
                    $"Order={string.Join(", ", pendingActions.Select(entry => $"{GetModifierBootstrapId(entry.Action)}[{entry.Key}]"))}");

                foreach ((ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction, string bootstrapKey) in pendingActions)
                {
                    string modifierId = GetModifierBootstrapId(bootstrapAction);
                    executedBootstrapKeys.Add(bootstrapKey);

                    ModLog.Info(
                        $"Running modifier bootstrap for {modifierId}. Pass={pass}/{maxBootstrapPasses}, Key={bootstrapKey}.");

                    await bootstrapAction.ApplyAsync();

                    if (ModifierBootstrapRequiresMidSettle(bootstrapAction))
                    {
                        await ChooseTheAncientHelpers.WaitForStartupModifierStateToSettleAsync(
                            runState,
                            orderedPlayers,
                            $"{reason} ({modifierId})");
                    }
                    else
                    {
                        await ChooseTheAncientHelpers.WaitForStartupModifierStateToSettleAsync(
                            runState,
                            orderedPlayers,
                            $"{reason} ({modifierId}, passive)",
                            requiredStableFrames: 2,
                            maxFrames: 90);
                    }

                    await ChooseTheAncientHelpers.WaitForAllPlayersToCompleteStartupStepAsync(
                        runState,
                        orderedPlayers,
                        flow,
                        startupBootstrapSyncEpoch,
                        bootstrapKey,
                        $"{reason} ({modifierId})");
                }
            }

            if (consecutiveEmptyPasses < 2)
            {
                ModLog.Warn(
                    $"CTA hit the startup modifier bootstrap pass limit before the action set stabilized. " +
                    $"Reason={reason}, LocalPlayer={localPlayer.NetId}, MaxPasses={maxBootstrapPasses}, " +
                    $"Executed={string.Join(", ", executedBootstrapKeys.OrderBy(value => value, StringComparer.Ordinal))}.");
            }
        }

        await ChooseTheAncientHelpers.WaitForStartupModifierStateToSettleAsync(
            runState,
            orderedPlayers,
            reason);
    }

    private static string GetModifierBootstrapId(
        ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    {
        string entry = bootstrapAction.Modifier?.Id.Entry ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(entry))
            return entry;

        return bootstrapAction.Modifier?.GetType().Name ?? "<unknown_modifier>";
    }

    private static string GetModifierBootstrapKey(
        RunState runState,
        ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    {
        ModifierModel? modifier = bootstrapAction.Modifier;
        if (modifier == null)
            return "<null_modifier>";

        int modifierIndex = runState.Modifiers
            .Select((candidate, index) => (candidate, index))
            .Where(entry => ReferenceEquals(entry.candidate, modifier))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();

        string modifierId = GetModifierBootstrapId(bootstrapAction);
        string modifierType = modifier.GetType().FullName ?? modifier.GetType().Name;

        return $"{modifierIndex}:{modifierId}:{modifierType}";
    }

    private static int GetModifierBootstrapPriority(
        ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    {
        string modifierId = GetModifierBootstrapId(bootstrapAction);

        if (string.Equals(modifierId, "SEALED_DECK", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(modifierId, "DRAFT", StringComparison.OrdinalIgnoreCase))
            return 1;

        return 100;
    }

    private static bool ModifierBootstrapRequiresMidSettle(
        ChooseTheAncientHelpers.ModifierBootstrapAction bootstrapAction)
    {
        string modifierId = GetModifierBootstrapId(bootstrapAction);

        return string.Equals(modifierId, "SEALED_DECK", StringComparison.OrdinalIgnoreCase)
               || string.Equals(modifierId, "DRAFT", StringComparison.OrdinalIgnoreCase);
    }

            
private static async Task<List<int>> CollectVotes(
    IReadOnlyList<Player> orderedPlayers,
    ChooseTheAncientSelectionScreen.RoundDefinition round,
    ChooseTheAncientSelectionScreen? localScreen,
    ChooseTheAncientFlowState? flow = null)
{
    if (orderedPlayers.Count == 0)
        throw new InvalidOperationException("No players available for vote collection.");

    RunState? runState = orderedPlayers[0].RunState as RunState;
    if (runState != null && RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
    {
        if (localScreen == null)
            throw new InvalidOperationException("Local ancient selection screen was not created.");

        int localVote = await localScreen.RunRoundAsync(round);
        localScreen.RecordVote(orderedPlayers[0], localVote);
        ModLog.Debug($"Vote received for player {orderedPlayers[0].NetId}: {localVote}");
        return [localVote];
    }

    Dictionary<ulong, uint> choiceIdsByPlayer = new();

    foreach (Player player in orderedPlayers)
    {
        uint choiceId = ReserveChoiceIdForCtaFlow(flow, player);
        choiceIdsByPlayer[player.NetId] = choiceId;
    }

    if (runState != null)
    {
        ChooseTheAncientHelpers.LogAct1StartupCheckpoint(
            "VoteRoundChoiceIdsReserved",
            runState,
            flow,
            orderedPlayers,
            $"RoundType={round.RoundType}, ChoiceIds={string.Join(", ", choiceIdsByPlayer.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
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

    
private static async Task<int> GetVoteForPlayer(
    Player player,
    uint choiceId,
    ChooseTheAncientSelectionScreen.RoundDefinition round,
    ChooseTheAncientSelectionScreen? localScreen)
{
    RunState? runState = player.RunState as RunState;
    bool isSinglePlayer = runState != null && RunManager.Instance.NetService.Type == NetGameType.Singleplayer;

    if (isSinglePlayer || ShouldSelectLocally(player))
    {
        if (localScreen == null)
        {
            throw new InvalidOperationException("Local ancient selection screen was not created.");
        }

        ModLog.Debug(
            $"CTA vote flow entering local selection path for player {player.NetId}. RoundType={round.RoundType}, ChoiceId={choiceId}, ChoiceIds={ChooseTheAncientHelpers.DescribeCurrentChoiceIds()}.");

        int localVote = await localScreen.RunRoundAsync(round);

        localScreen.RecordVote(player, localVote);

        if (!isSinglePlayer)
        {
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                player,
                choiceId,
                PlayerChoiceResult.FromIndex(localVote));
        }

        ModLog.Debug(
            $"CTA vote flow sent local vote for player {player.NetId}. RoundType={round.RoundType}, ChoiceId={choiceId}, Vote={localVote}.");

        return localVote;
    }

    ModLog.Debug(
        $"CTA vote flow waiting for remote vote from player {player.NetId}. RoundType={round.RoundType}, ChoiceId={choiceId}, ChoiceIds={ChooseTheAncientHelpers.DescribeCurrentChoiceIds()}.");

    int remoteVote = (await RunManager.Instance.PlayerChoiceSynchronizer
            .WaitForRemoteChoice(player, choiceId))
        .AsIndex();

    ModLog.Debug(
        $"CTA vote flow received remote vote from player {player.NetId}. RoundType={round.RoundType}, ChoiceId={choiceId}, Vote={remoteVote}.");

    localScreen?.RecordVote(player, remoteVote);
    return remoteVote;
}

    private static bool ShouldSelectLocally(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            return RunManager.Instance.NetService.Type != NetGameType.Replay;
        }

        return false;
    }

    private static (AncientEventModel? suppressedPreviewAncient, AncientEventModel? reactionAncient, string? suppressedPreviewAncientId, string? reactionAncientId) ResolveSecondRoundPresentation(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> firstRoundPool,
        IReadOnlyList<AncientEventModel> finalists,
        IReadOnlyList<int> firstVotes)
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

        AncientEventModel suppressedPreviewAncient;
        int leftCount = finalistVoteCounts[finalists[0].Id.Entry];
        int rightCount = finalistVoteCounts[finalists[1].Id.Entry];

        if (leftCount == rightCount)
        {
            var rng = ChooseTheAncientHelpers.CreateSecondRoundPresentationRng(runState, nextActIndex);
            suppressedPreviewAncient = finalists[rng.NextInt(finalists.Count)];
        }
        else
        {
            suppressedPreviewAncient = leftCount > rightCount
                ? finalists[0]
                : finalists[1];
        }

        AncientEventModel reactionAncient = finalists
            .First(ancient => ancient.Id.Entry != suppressedPreviewAncient.Id.Entry);

        ModLog.Debug($"Second vote presentation decided from round-one votes: suppress={suppressedPreviewAncient.Id.Entry}, reaction={reactionAncient.Id.Entry}, voteCounts={leftCount}/{rightCount}");
        // return SuppressedPreviewAncient to pass on to the selection screen
        return (suppressedPreviewAncient, reactionAncient, suppressedPreviewAncient.Id.Entry, reactionAncient.Id.Entry);
    }
    
    private static int ResolveSecondPlaceIndex(
        RunState runState,
        int nextActIndex,
        int optionCount,
        int firstPlaceIndex,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    {
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
            firstPlaceIndex,
            votesInPlayerSlotOrder);

        int chosenLeader = leaders[rng.NextInt(leaders.Count)];

        if (ModLog.IsDebugEnabled)
        {
            string countSummary = string.Join(
                ", ",
                nonWinnerCounts
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            string tiedLeaders = string.Join(",", leaders);
            ModLog.Debug(
                $"Second-place tie for act {nextActIndex + 1} after excluding first-place index {firstPlaceIndex}. " +
                $"Counts={countSummary}; tied leaders=[{tiedLeaders}]; selected={chosenLeader}.");
        }

        return chosenLeader;
    }

    private static Rng CreateSecondPlaceTieBreakRng(
        RunState runState,
        int nextActIndex,
        int firstPlaceIndex,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    {
        // Change the seed based on who was the first picked winner
        Rng baseRng = ChooseTheAncientHelpers.CreateFinalVoteResolutionRng(runState, nextActIndex);
        string voteSignature = $"{firstPlaceIndex}|{string.Join(",", votesInPlayerSlotOrder)}";
        uint voteHash = unchecked((uint)StringHelper.GetDeterministicHashCode($"SecondPlace|{voteSignature}"));
        return new Rng(unchecked(baseRng.Seed + voteHash));
    }

    private static int ResolveMostVotedIndex(
        RunState runState,
        int nextActIndex,
        int optionCount,
        IReadOnlyList<int> votesInPlayerSlotOrder)
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

    private static async Task<IReadOnlyList<int>?> GetEffectiveAncientPoolSourceActsAsync(
        IReadOnlyList<Player> orderedPlayers,
        int targetActIndex,
        ChooseTheAncientFlowState? flow = null)
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

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
        uint choiceId = ReserveChoiceIdForCtaFlow(flow, hostPlayer);

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
    int targetActIndex,
    ChooseTheAncientFlowState? flow = null)
{
    ChooseTheAncientConfig.RefreshFromModConfig();

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
    uint choiceId = ReserveChoiceIdForCtaFlow(flow, hostPlayer);

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
        IReadOnlyList<Player> orderedPlayers,
        ChooseTheAncientFlowState? flow = null)
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            return ChooseTheAncientConfig.GameMode;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = ReserveChoiceIdForCtaFlow(flow, hostPlayer);

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

    private static async Task<int> GetEffectiveAncientCountAsync(
        IReadOnlyList<Player> orderedPlayers,
        ChooseTheAncientFlowState? flow = null)
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            ModLog.Debug($"Using local AncientCount={ChooseTheAncientConfig.AncientCount}");
            return ChooseTheAncientConfig.AncientCount;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = ReserveChoiceIdForCtaFlow(flow, hostPlayer);

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
