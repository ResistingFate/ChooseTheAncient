using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Messages;
using ChooseTheAncient.ChooseTheAncientCode.Patches;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal static class ChooseTheAncientConsoleDebugState
{
    private static readonly object SelectionResolutionHandlerLock = new();

    private static INetGameService? _registeredSelectionResolutionHandlerService;
    private static MessageHandlerDelegate<ChooseTheAncientConsoleSelectionResolutionMessage>?
        _registeredSelectionResolutionHandler;

    private static int _ballotRequestId;

    public static bool ShowSuppressedContent { get; private set; }

    public static bool ToggleSuppressedContent()
    {
        ShowSuppressedContent = !ShowSuppressedContent;
        return ShowSuppressedContent;
    }

    public static int BeginBallotRequest()
    {
        return Interlocked.Increment(ref _ballotRequestId);
    }

    public static bool IsCurrentBallotRequest(int requestId)
    {
        return Volatile.Read(ref _ballotRequestId) == requestId;
    }

    public static RunState? GetRunState(Player? issuingPlayer)
    {
        return issuingPlayer?.RunState as RunState
               ?? ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
    }

    public static void EnsureSelectionResolutionHandlerRegistered()
    {
        INetGameService? currentService = RunManager.Instance.NetService;
        if (currentService == null)
        {
            return;
        }

        lock (SelectionResolutionHandlerLock)
        {
            if (ReferenceEquals(
                    _registeredSelectionResolutionHandlerService,
                    currentService)
                && _registeredSelectionResolutionHandler != null)
            {
                return;
            }

            if (_registeredSelectionResolutionHandlerService != null
                && _registeredSelectionResolutionHandler != null)
            {
                try
                {
                    _registeredSelectionResolutionHandlerService.UnregisterMessageHandler(
                        _registeredSelectionResolutionHandler);
                }
                catch (Exception ex)
                {
                    ModLog.Warn(
                        "Could not unregister CTA's console selection-resolution handler " +
                        $"from the previous net service: {ex.GetType().Name}: {ex.Message}");
                }
            }

            MessageHandlerDelegate<ChooseTheAncientConsoleSelectionResolutionMessage> handler =
                HandleRemoteSelectionResolution;
            currentService.RegisterMessageHandler(handler);
            _registeredSelectionResolutionHandlerService = currentService;
            _registeredSelectionResolutionHandler = handler;

            ModLog.Debug("Registered CTA console selection-resolution message handler.");
        }
    }

    public static bool TryApplySelectionResolution(
        RunState runState,
        int targetActIndex,
        ConsoleSelectionResolution resolution,
        string source)
    {
        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);

        flow.RequestConsoleSelectionResolution(targetActIndex, resolution);

        if (ChooseTheAncientSelectionScreen.TryResolveCurrentRoundForConsoleResolution(
                targetActIndex))
        {
            int closedScreens = resolution == ConsoleSelectionResolution.CancelFlow
                ? ChooseTheAncientSelectionScreen.CloseOpenScreensForConsoleReplacement()
                : 0;

            ModLog.Info(
                $"Applied CTA console selection {DescribeResolution(resolution)} for act " +
                $"{targetActIndex + 1}. Source={source}. " +
                $"ClosedScreens={closedScreens}.");
            return true;
        }

        if (flow.ResolvedActs.Contains(targetActIndex)
            && !flow.FlowInProgress
            && !flow.ConsoleNavigationInProgress)
        {
            flow.ClearConsoleSelectionResolution();
            ModLog.Warn(
                $"Ignored stale CTA console selection {DescribeResolution(resolution)} for act " +
                $"{targetActIndex + 1} because that act is already resolved. Source={source}.");
            return false;
        }

        ModLog.Info(
            $"Queued CTA console selection {DescribeResolution(resolution)} for act " +
            $"{targetActIndex + 1}; the matching local ballot has not opened yet. " +
            $"Source={source}.");
        return true;
    }

    public static bool TryResolveActiveSelectionForAllPeers(
        RunState runState,
        ConsoleSelectionResolution resolution,
        string source,
        out int targetActIndex)
    {
        if (!ChooseTheAncientSelectionScreen.TryGetCurrentSelectionTargetActIndex(
                out targetActIndex))
        {
            if (resolution != ConsoleSelectionResolution.CancelFlow)
            {
                return false;
            }

            ChooseTheAncientFlowState flow =
                ChooseTheAncientStateStore.Get(runState);

            if ((!flow.FlowInProgress && !flow.ConsoleNavigationInProgress)
                || !flow.ActiveFlowTargetActIndex.HasValue)
            {
                return false;
            }

            targetActIndex = flow.ActiveFlowTargetActIndex.Value;
        }

        EnsureSelectionResolutionHandlerRegistered();

        if (!TryApplySelectionResolution(
                runState,
                targetActIndex,
                resolution,
                source))
        {
            return false;
        }

        INetGameService netService = RunManager.Instance.NetService;
        if (netService.IsConnected)
        {
            netService.SendMessage(
                ChooseTheAncientConsoleSelectionResolutionMessage.Create(
                    targetActIndex,
                    resolution));
        }

        return true;
    }

    private static void HandleRemoteSelectionResolution(
        ChooseTheAncientConsoleSelectionResolutionMessage message,
        ulong senderId)
    {
        RunState? runState = GetRunState(issuingPlayer: null);
        if (runState == null || !RunManager.Instance.IsInProgress)
        {
            ModLog.Warn(
                $"Ignored remote CTA selection resolution from {senderId} because no run is active.");
            return;
        }

        TryApplySelectionResolution(
            runState,
            message.targetActIndex,
            message.Resolution,
            $"network sender {senderId}");
    }

    private static string DescribeResolution(ConsoleSelectionResolution resolution)
    {
        return resolution == ConsoleSelectionResolution.CancelFlow
            ? "cancellation"
            : "skip";
    }
}

/// <summary>
/// Shared implementation for ctaact and ctastay. This abstract base is not
/// registered as a console command; only its concrete subclasses have names.
/// </summary>
public abstract class ChooseTheAncientBallotConsoleCmdBase : AbstractConsoleCmd
{
    protected abstract string Usage { get; }

    public sealed override bool IsNetworked => true;

    public sealed override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length != 1)
        {
            return new CmdResult(success: false, $"Use {Usage}.");
        }

        if (!int.TryParse(args[0], out int actNumber))
        {
            return new CmdResult(
                success: false,
                $"The argument must be an act number, got '{args[0]}'.");
        }

        RunState? runState =
            ChooseTheAncientConsoleDebugState.GetRunState(issuingPlayer);
        if (runState == null || !RunManager.Instance.IsInProgress)
        {
            return new CmdResult(success: false, "This command only works during a run.");
        }

        if (issuingPlayer == null)
        {
            return new CmdResult(
                success: false,
                "Could not identify the player issuing the command.");
        }

        int actCount = runState.Acts.Count;
        if (actNumber < 1 || actNumber > actCount)
        {
            return new CmdResult(
                success: false,
                $"Select an act number between 1 and {actCount}.");
        }

        ChooseTheAncientConsoleDebugState.EnsureSelectionResolutionHandlerRegistered();

        int requestId = ChooseTheAncientConsoleDebugState.BeginBallotRequest();
        Task task = StartBallotAsync(
            runState,
            requestId,
            actNumber - 1);

        return new CmdResult(task, success: true, GetSuccessMessage(actNumber));
    }

    public sealed override CompletionResult GetArgumentCompletions(
        Player? player,
        string[] args)
    {
        if (args.Length > 1)
        {
            return new CompletionResult
            {
                Type = CompletionType.Argument,
                ArgumentContext = CmdName,
                ArgumentIndex = args.Length - 1,
                CommandPrefix = BuildPrefix(args[..^1])
            };
        }

        RunState? runState = ChooseTheAncientConsoleDebugState.GetRunState(player);
        int actCount = Math.Max(1, runState?.Acts.Count ?? 3);
        string[] candidates = new string[actCount];

        for (int i = 0; i < candidates.Length; i++)
        {
            candidates[i] = (i + 1).ToString();
        }

        return CompleteArgument(
            candidates,
            Array.Empty<string>(),
            args.Length == 0 ? string.Empty : args[0]);
    }

    protected abstract Task StartBallotAsync(
        RunState runState,
        int requestId,
        int actIndex);

    protected abstract string GetSuccessMessage(int actNumber);
}

public sealed class ChooseTheAncientActConsoleCmd : ChooseTheAncientBallotConsoleCmdBase
{
    public override string CmdName => "ctaact";
    public override string Args => "<int: act>";
    public override string Description =>
        "Jumps to an act, opens its CTA ballot, then enters the chosen ancient room.";

    protected override string Usage => "ctaact <act>";

    protected override Task StartBallotAsync(
        RunState runState,
        int requestId,
        int actIndex)
    {
        return ChooseTheAncientConsoleBallotRunner.NavigateAndOpenAsync(
            runState,
            requestId,
            actIndex);
    }

    protected override string GetSuccessMessage(int actNumber)
    {
        return $"Navigating to act {actNumber}, opening its CTA ballot, then entering the chosen ancient room.";
    }
}

public sealed class ChooseTheAncientStayConsoleCmd : ChooseTheAncientBallotConsoleCmdBase
{
    public override string CmdName => "ctastay";
    public override string Args => "<int: ballot act>";
    public override string Description =>
        "Uses an act's CTA ballot for the current act, then enters the chosen ancient room.";

    protected override string Usage => "ctastay <ballot-act>";

    protected override Task StartBallotAsync(
        RunState runState,
        int requestId,
        int actIndex)
    {
        return ChooseTheAncientConsoleBallotRunner.OpenInPlaceAsync(
            runState,
            requestId,
            actIndex);
    }

    protected override string GetSuccessMessage(int actNumber)
    {
        return $"Opening the act {actNumber} CTA ballot for the current act, then entering the chosen ancient room.";
    }
}

internal static class ChooseTheAncientConsoleBallotRunner
{
    public static async Task NavigateAndOpenAsync(
        RunState runState,
        int requestId,
        int actIndex)
    {
        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        if (!await ReleasePreviousConsoleFlowAsync(
                runState,
                flow,
                requestId,
                "ctaact"))
        {
            return;
        }

        BeginConsoleBallotOperation(flow, actIndex);

        bool act1WasResolved = false;
        bool act1WasTriggered = false;
        bool forceNeowBlessingModeBeforeAct1 = flow.ForceNeowBlessingMode;

        try
        {
            if (actIndex != 0)
            {
                await ChooseTheAncientCoordinator.EnsureConsoleModifierBootstrapAsync(
                    runState,
                    flow);
            }

            if (ShouldCancelBeforeBallot(flow, requestId, actIndex))
            {
                ModLog.Info(
                    $"Canceled ctaact before navigating to act {actIndex + 1}.");
                return;
            }

            if (actIndex == 0)
            {
                // EnterAct must see Act 1 as unresolved so CreateRoomPatch creates CTA's shell.
                act1WasResolved = flow.ResolvedActs.Remove(0);
                act1WasTriggered = flow.Act1StartingRoomFlowTriggered;
                flow.Act1StartingRoomFlowTriggered = false;
                flow.ForceNeowBlessingMode = false;
                flow.RequestSuppressNextAct1StartingRoomFlow();
            }

            NMapScreen.Instance?.SetTravelEnabled(enabled: true);
            await RunManager.Instance.EnterAct(actIndex);
            ActConsoleCmdNavigationPatch.ClearStaleTransitionState("ctaact");

            // Let EnterAct finish replacing the room and screen stack.
            await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

            if (ShouldCancelBeforeBallot(flow, requestId, actIndex))
            {
                ModLog.Info(
                    $"Canceled ctaact before opening the act {actIndex + 1} ballot.");
                RestoreAct1CommandStateIfNeeded(
                    flow,
                    actIndex,
                    act1WasResolved,
                    act1WasTriggered,
                    forceNeowBlessingModeBeforeAct1);
                return;
            }

            if (actIndex == 0
                && ChooseTheAncientHelpers.IsAct1StartingMapPoint(runState)
                && runState.CurrentRoom is ChooseTheAncientStartRoom)
            {
                flow.FlowInProgress = true;
                flow.ConsoleNavigationInProgress = false;
                flow.Act1StartingRoomFlowTriggered = true;

                await ChooseTheAncientCoordinator.RunAct1StartingRoomFlowAsync(
                    runState,
                    flow);

                bool completed = flow.ResolvedActs.Contains(0);
                if (!completed)
                {
                    RestoreAct1CommandStateIfNeeded(
                        flow,
                        actIndex,
                        act1WasResolved,
                        act1WasTriggered,
                        forceNeowBlessingModeBeforeAct1);
                }

                return;
            }

            if (actIndex == 0)
            {
                ModLog.Warn(
                    "ctaact 1 did not enter CTA's Act 1 starting shell. " +
                    "Opening a normal in-place Act 1 ballot instead.");

                flow.ForceNeowBlessingMode =
                    forceNeowBlessingModeBeforeAct1;
                flow.Act1StartingRoomFlowTriggered = act1WasTriggered;
                if (act1WasResolved)
                {
                    flow.ResolvedActs.Add(0);
                }

                await ChooseTheAncientCoordinator.EnsureConsoleModifierBootstrapAsync(
                    runState,
                    flow);
            }

            await RunGenericConsoleBallotAsync(
                runState,
                flow,
                requestId,
                ballotActIndex: actIndex,
                applyToActIndex: actIndex,
                commandName: "ctaact");
        }
        catch
        {
            RestoreAct1CommandStateIfNeeded(
                flow,
                actIndex,
                act1WasResolved,
                act1WasTriggered,
                forceNeowBlessingModeBeforeAct1);
            throw;
        }
        finally
        {
            FinishConsoleBallotOperation(runState, flow, "ctaact");
        }
    }

    public static async Task OpenInPlaceAsync(
        RunState runState,
        int requestId,
        int ballotActIndex,
        string commandName = "ctastay")
    {
        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        if (!await ReleasePreviousConsoleFlowAsync(
                runState,
                flow,
                requestId,
                commandName))
        {
            return;
        }

        // The requested act supplies the ballot; the current act receives the winner.
        int applyToActIndex = runState.CurrentActIndex;
        BeginConsoleBallotOperation(flow, ballotActIndex);

        try
        {
            await ChooseTheAncientCoordinator.EnsureConsoleModifierBootstrapAsync(
                runState,
                flow);

            // Let the console hide before the shared path closes the map and
            // pushes the ballot overlay.
            await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

            if (ShouldCancelBeforeBallot(flow, requestId, ballotActIndex))
            {
                ModLog.Info(
                    $"Canceled {commandName} before opening the act {ballotActIndex + 1} ballot.");
                return;
            }

            await RunGenericConsoleBallotAsync(
                runState,
                flow,
                requestId,
                ballotActIndex,
                applyToActIndex,
                commandName: commandName);
        }
        finally
        {
            FinishConsoleBallotOperation(runState, flow, commandName);
        }
    }

    private static void BeginConsoleBallotOperation(
        ChooseTheAncientFlowState flow,
        int ballotActIndex)
    {
        // Immediate multiplayer skip/cancel messages may arrive before the ballot opens.
        flow.ConsoleNavigationInProgress = true;
        flow.ActiveFlowTargetActIndex = ballotActIndex;
    }

    private static void FinishConsoleBallotOperation(
        RunState runState,
        ChooseTheAncientFlowState flow,
        string commandName)
    {
        flow.ClearSuppressNextAct1StartingRoomFlow();
        flow.ConsoleNavigationInProgress = false;

        if (!flow.FlowInProgress)
        {
            flow.ClearConsoleSelectionResolution();
            flow.ActiveFlowTargetActIndex = null;
        }

        flow.ConsoleMapSelectionRebasePending = true;
        TryApplyPendingMapSelectionRebase(
            runState,
            flow,
            commandName);
        RefreshCurrentAncientMapNodes(runState, commandName);
    }

    private static bool ShouldCancelBeforeBallot(
        ChooseTheAncientFlowState flow,
        int requestId,
        int ballotActIndex)
    {
        return !ChooseTheAncientConsoleDebugState.IsCurrentBallotRequest(requestId)
               || flow.ConsumeConsoleSelectionResolution(
                       ballotActIndex,
                       ConsoleSelectionResolution.CancelFlow);
    }

    private static async Task RunGenericConsoleBallotAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int requestId,
        int ballotActIndex,
        int applyToActIndex,
        string commandName)
    {
        if (ShouldCancelBeforeBallot(flow, requestId, ballotActIndex))
        {
            return;
        }

        await CloseMapBeforeConsoleBallotAsync(commandName);

        bool targetWasResolved =
            flow.ResolvedActs.Remove(applyToActIndex);
        bool previousForceNeowBlessingMode = flow.ForceNeowBlessingMode;
        bool enteredAncientRoom = false;

        if (applyToActIndex == 0)
        {
            flow.ForceNeowBlessingMode = false;
        }

        flow.FlowInProgress = true;
        flow.ConsoleNavigationInProgress = false;

        try
        {
            ChooseTheAncientFlowCompletion completion;
            try
            {
                completion = await ChooseTheAncientCoordinator.RunAsync(
                    RunManager.Instance,
                    runState,
                    ballotActIndex,
                    flow,
                    consoleLocationActIndex: runState.CurrentActIndex,
                    consoleApplyToActIndex: applyToActIndex);
            }
            catch
            {
                RestoreGenericBallotState(
                    flow,
                    applyToActIndex,
                    targetWasResolved,
                    previousForceNeowBlessingMode);
                throw;
            }

            if (completion != ChooseTheAncientFlowCompletion.Completed)
            {
                RestoreGenericBallotState(
                    flow,
                    applyToActIndex,
                    targetWasResolved,
                    previousForceNeowBlessingMode);
                return;
            }

            await EnterChosenAncientRoomDebugAsync(
                runState,
                applyToActIndex,
                commandName);
            enteredAncientRoom = true;
        }
        finally
        {
            if (!enteredAncientRoom)
            {
                RestoreMapAfterConsoleBallotIfNeeded(
                    runState,
                    requestId,
                    commandName);
            }
        }
    }

    private static async Task EnterChosenAncientRoomDebugAsync(
        RunState runState,
        int applyToActIndex,
        string commandName)
    {
        if (runState.CurrentActIndex != applyToActIndex)
        {
            throw new InvalidOperationException(
                $"{commandName} cannot enter the chosen ancient room for act " +
                $"{applyToActIndex + 1} while the run is in act " +
                $"{runState.CurrentActIndex + 1}.");
        }

        AncientEventModel chosenAncient =
            ChooseTheAncientHelpers.GetChosenAncient(runState.Act);
        MapCoord ancientCoord = runState.Map.StartingMapPoint.coord;

        bool iconUpdated =
            ChooseTheAncientHelpers.ApplyChosenAncientIconToStartingMapPoint(
                runState,
                chosenAncient);

        if (!iconUpdated)
        {
            ModLog.Warn(
                $"{commandName} could not update the starting Ancient node icon " +
                $"to {chosenAncient.Id.Entry} before entering the room.");
        }

        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

        bool ancientNodeWasUnvisited =
            !runState.VisitedMapCoords.Contains(ancientCoord);

        if (ancientNodeWasUnvisited)
        {
            runState.ActFloor = ancientCoord.row + 1;
        }

        await RunManager.Instance.EnterMapCoordDebug(
            ancientCoord,
            RoomType.Event,
            MapPointType.Ancient,
            chosenAncient,
            showTransition: true);

        ModLog.Info(
            $"{commandName} entered act {applyToActIndex + 1}'s chosen ancient " +
            $"room for {chosenAncient.Id.Entry}. " +
            $"AncientNodeMarkedVisited={ancientNodeWasUnvisited}, " +
            $"MapLocation={runState.MapLocation}.");
    }

    private static async Task CloseMapBeforeConsoleBallotAsync(
        string commandName)
    {
        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen?.IsOpen != true)
        {
            return;
        }

        mapScreen.Close(animateOut: false);
        await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);

        ModLog.Info(
            $"{commandName} closed the map screen before opening the CTA overlay.");
    }

    private static void RestoreMapAfterConsoleBallotIfNeeded(
        RunState runState,
        int requestId,
        string commandName)
    {
        if (!ChooseTheAncientConsoleDebugState.IsCurrentBallotRequest(requestId)
            || runState.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.MapRoom)
        {
            return;
        }

        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen == null || mapScreen.IsOpen)
        {
            return;
        }

        mapScreen.Open(isOpenedFromTopBar: true);
        ModLog.Info(
            $"{commandName} restored the map screen after the CTA ballot closed.");
    }

    internal static bool TryApplyPendingMapSelectionRebase(
        RunState runState,
        ChooseTheAncientFlowState flow,
        string commandName,
        NMapScreen? mapScreen = null)
    {
        if (!flow.ConsoleMapSelectionRebasePending)
        {
            return false;
        }

        mapScreen ??= NMapScreen.Instance;
        if (mapScreen?.IsOpen != true)
        {
            return false;
        }

        RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(
            runState.MapLocation);
        mapScreen.PlayerVoteDictionary.Clear();
        mapScreen.RefreshAllMapPointVotes();
        flow.ConsoleMapSelectionRebasePending = false;

        ModLog.Info(
            $"{commandName} rebased map selection synchronization to " +
            $"{runState.MapLocation}.");
        return true;
    }

    private static void RefreshCurrentAncientMapNodes(
        RunState runState,
        string commandName)
    {
        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen == null)
        {
            return;
        }

        AncientEventModel chosenAncient =
            ChooseTheAncientHelpers.GetChosenAncient(runState.Act);
        int refreshedCount = RefreshAncientMapNodesRecursive(
            mapScreen,
            chosenAncient,
            runState);

        if (refreshedCount == 0)
        {
            return;
        }

        if (mapScreen.IsOpen)
        {
            mapScreen.RefreshAllPointVisuals();
        }

        ModLog.Info(
            $"{commandName} refreshed {refreshedCount} ancient map node(s) " +
            $"for {chosenAncient.Id.Entry} in act " +
            $"{runState.CurrentActIndex + 1}.");
    }

    private static int RefreshAncientMapNodesRecursive(
        Node parent,
        AncientEventModel chosenAncient,
        RunState runState)
    {
        int refreshedCount = 0;

        foreach (Node child in parent.GetChildren())
        {
            if (child is NAncientMapPoint ancientMapPoint)
            {
                TextureRect? icon =
                    ancientMapPoint.GetNodeOrNull<TextureRect>("Icon");
                TextureRect? outline =
                    ancientMapPoint.GetNodeOrNull<TextureRect>("Icon/Outline");

                if (icon != null)
                {
                    icon.Texture = chosenAncient.MapIcon;
                }

                if (outline != null)
                {
                    outline.Texture = chosenAncient.MapIconOutline;
                    outline.Modulate = runState.Act.MapBgColor;
                }

                refreshedCount++;
            }

            refreshedCount += RefreshAncientMapNodesRecursive(
                child,
                chosenAncient,
                runState);
        }

        return refreshedCount;
    }

    private static void RestoreGenericBallotState(
        ChooseTheAncientFlowState flow,
        int applyToActIndex,
        bool targetWasResolved,
        bool previousForceNeowBlessingMode)
    {
        flow.ForceNeowBlessingMode = previousForceNeowBlessingMode;

        if (targetWasResolved)
        {
            flow.ResolvedActs.Add(applyToActIndex);
        }
    }

    private static void RestoreAct1CommandStateIfNeeded(
        ChooseTheAncientFlowState flow,
        int actIndex,
        bool act1WasResolved,
        bool act1WasTriggered,
        bool forceNeowBlessingModeBeforeAct1)
    {
        if (actIndex != 0)
        {
            return;
        }

        flow.ForceNeowBlessingMode =
            forceNeowBlessingModeBeforeAct1;
        flow.Act1StartingRoomFlowTriggered =
            act1WasTriggered;

        if (act1WasResolved)
        {
            flow.ResolvedActs.Add(0);
        }
    }

    internal static async Task<bool> ReleasePreviousConsoleFlowAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int requestId,
        string commandName)
    {
        bool loggedWait = false;
        int? canceledTargetActIndex = null;

        while (flow.FlowInProgress
               || flow.ConsoleNavigationInProgress
               || ChooseTheAncientSelectionScreen.HasUnclosedConsoleSelectionScreen())
        {
            if (!ChooseTheAncientConsoleDebugState.IsCurrentBallotRequest(
                    requestId))
            {
                return false;
            }

            if (!loggedWait)
            {
                ModLog.Info(
                    $"A previous CTA flow or screen is still active. {commandName} will " +
                    "close it and continue with the newest request after cleanup.");
                loggedWait = true;
            }

            int? activeTargetActIndex = GetActiveTargetActIndex(flow);

            if (activeTargetActIndex.HasValue
                && canceledTargetActIndex != activeTargetActIndex.Value)
            {
                if (!flow.IsConsoleSelectionResolutionRequestedFor(
                        activeTargetActIndex.Value,
                        ConsoleSelectionResolution.CancelFlow))
                {
                    ChooseTheAncientConsoleDebugState.TryApplySelectionResolution(
                        runState,
                        activeTargetActIndex.Value,
                        ConsoleSelectionResolution.CancelFlow,
                        $"superseding {commandName} command");
                }

                canceledTargetActIndex = activeTargetActIndex.Value;
            }

            int closedScreens =
                ChooseTheAncientSelectionScreen.CloseOpenScreensForConsoleReplacement();
            if (closedScreens > 0)
            {
                ModLog.Info(
                    $"Closed {closedScreens} previous CTA selection screen(s) before " +
                    $"opening the replacement {commandName} ballot.");
            }

            await ChooseTheAncientHelpers.WaitForProcessFramesAsync(1);
        }

        return ChooseTheAncientConsoleDebugState.IsCurrentBallotRequest(
            requestId);
    }

    private static int? GetActiveTargetActIndex(
        ChooseTheAncientFlowState flow)
    {
        return ChooseTheAncientSelectionScreen.TryGetCurrentSelectionTargetActIndex(
            out int targetActIndex)
            ? targetActIndex
            : flow.ActiveFlowTargetActIndex;
    }

}

public sealed class ChooseTheAncientSkipSelectionConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "ctaskip";
    public override string Args => "";
    public override string Description =>
        "Skips the active CTA ballot for every peer and preserves the vanilla/default ancient.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length != 0)
        {
            return new CmdResult(success: false, "This command does not take arguments.");
        }

        RunState? runState = ChooseTheAncientConsoleDebugState.GetRunState(issuingPlayer);
        if (runState == null || !RunManager.Instance.IsInProgress)
        {
            return new CmdResult(success: false, "This command only works during a run.");
        }

        if (!ChooseTheAncientConsoleDebugState.TryResolveActiveSelectionForAllPeers(
                runState,
                ConsoleSelectionResolution.SkipBallot,
                "local console",
                out int targetActIndex))
        {
            return new CmdResult(
                success: false,
                "No active ChooseTheAncient selection round was available to skip.");
        }

        return new CmdResult(
            success: true,
            $"Skipped the act {targetActIndex + 1} CTA ballot for all peers; " +
            "the vanilla/default ancient will be kept.");
    }
}

public sealed class ChooseTheAncientShowSuppressedConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "ctaffsay";
    public override string Args => "";
    public override string Description =>
        "Toggles whether suppressed relic options and dialogue are shown on CTA final rounds.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length != 0)
        {
            return new CmdResult(success: false, "This command does not take arguments.");
        }

        bool show =
            ChooseTheAncientConsoleDebugState.ToggleSuppressedContent();

        int refreshedScreens =
            ChooseTheAncientSelectionScreen.RefreshConsoleSuppressedContentOverride();

        return new CmdResult(
            success: true,
            $"Suppressed CTA relic options and dialogue are now {(show ? "shown" : "hidden normally")}. " +
            $"Refreshed {refreshedScreens} open screen(s).");
    }
}
