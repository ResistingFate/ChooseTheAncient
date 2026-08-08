using System;
using System.Collections.Generic;
using System.Linq;

namespace ChooseTheAncient.ChooseTheAncientCode;

public enum StartupStepRecordResult
{
    Added,
    Updated,
    Duplicate
}


internal enum ConsoleSelectionResolution
{
    SkipBallot,
    CancelFlow
}

public sealed class StartupStepCompletionInfo
{
    public required int TotalStepCount { get; init; }
    public required string ModifierId { get; init; }
    public required uint NextChoiceId { get; init; }
}

internal readonly record struct ConsoleSelectionResolutionRequest(
    int TargetActIndex,
    ConsoleSelectionResolution Resolution);

public sealed class ChooseTheAncientFlowState
{
    public HashSet<int> ResolvedActs { get; } = new();
    public bool FlowInProgress { get; set; }
    public bool ContinueEnterNextAct { get; set; }
    public bool ModifierBootstrapCompleted { get; set; }
    public bool ForceNeowBlessingMode { get; set; }
    private ConsoleSelectionResolutionRequest? _consoleSelectionResolutionRequest;

    public int? ActiveFlowTargetActIndex { get; set; }
    public bool ConsoleNavigationInProgress { get; set; }
    public bool ConsoleMapSelectionRebasePending { get; set; }
    public HashSet<int> StartingRoomFlowTriggeredActs { get; } = new();
    public int? PendingVanillaMapRoomReplacementActIndex { get; set; }
    private bool SuppressNextAct1StartingRoomFlow { get; set; }

    public bool ForceAct1NeowBlessingMode
    {
        get => ForceNeowBlessingMode;
        set => ForceNeowBlessingMode = value;
    }

    public bool Act1StartingRoomFlowTriggered { get; set; }

    public int Act1StartupBootstrapSyncEpoch { get; private set; }

    internal void RequestConsoleSelectionResolution(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        _consoleSelectionResolutionRequest =
            new ConsoleSelectionResolutionRequest(targetActIndex, resolution);
    }

    internal bool IsConsoleSelectionResolutionRequestedFor(int targetActIndex)
    {
        return _consoleSelectionResolutionRequest?.TargetActIndex == targetActIndex;
    }

    internal bool IsConsoleSelectionResolutionRequestedFor(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        return _consoleSelectionResolutionRequest is
        {
            TargetActIndex: var requestedTargetActIndex,
            Resolution: var requestedResolution
        }
        && requestedTargetActIndex == targetActIndex
        && requestedResolution == resolution;
    }

    internal bool ConsumeConsoleSelectionResolution(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        if (_consoleSelectionResolutionRequest is not
            {
                TargetActIndex: var requestedTargetActIndex,
                Resolution: var requestedResolution
            }
            || requestedTargetActIndex != targetActIndex
            || requestedResolution != resolution)
        {
            return false;
        }

        _consoleSelectionResolutionRequest = null;
        return true;
    }

    internal void ClearConsoleSelectionResolution()
    {
        _consoleSelectionResolutionRequest = null;
    }

    public void RequestSuppressNextAct1StartingRoomFlow()
    {
        SuppressNextAct1StartingRoomFlow = true;
    }

    public bool ConsumeSuppressNextAct1StartingRoomFlow()
    {
        bool suppress = SuppressNextAct1StartingRoomFlow;
        SuppressNextAct1StartingRoomFlow = false;
        return suppress;
    }

    public void ClearSuppressNextAct1StartingRoomFlow()
    {
        SuppressNextAct1StartingRoomFlow = false;
    }

    public Dictionary<int, Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>> PendingStartupStepCompletionMessagesByEpoch { get; } = new();

    public int BeginAct1StartupBootstrapSyncEpoch()
    {
        Act1StartupBootstrapSyncEpoch++;
        PendingStartupStepCompletionMessagesByEpoch.Clear();
        return Act1StartupBootstrapSyncEpoch;
    }

    public StartupStepRecordResult RecordPendingStartupStepCompletionMessage(
        int syncEpoch,
        int stepIndex,
        ulong playerNetId,
        int totalStepCount,
        string modifierId,
        uint nextChoiceId)
    {
        if (!PendingStartupStepCompletionMessagesByEpoch.TryGetValue(syncEpoch, out Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>? byStep))
        {
            byStep = new Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>();
            PendingStartupStepCompletionMessagesByEpoch[syncEpoch] = byStep;
        }

        if (!byStep.TryGetValue(stepIndex, out Dictionary<ulong, StartupStepCompletionInfo>? byPlayer))
        {
            byPlayer = new Dictionary<ulong, StartupStepCompletionInfo>();
            byStep[stepIndex] = byPlayer;
        }

        StartupStepCompletionInfo incoming = new()
        {
            TotalStepCount = totalStepCount,
            ModifierId = modifierId,
            NextChoiceId = nextChoiceId
        };

        if (!byPlayer.TryGetValue(playerNetId, out StartupStepCompletionInfo? existing))
        {
            byPlayer[playerNetId] = incoming;
            return StartupStepRecordResult.Added;
        }

        if (existing.TotalStepCount == incoming.TotalStepCount
            && string.Equals(existing.ModifierId, incoming.ModifierId, StringComparison.Ordinal)
            && existing.NextChoiceId == incoming.NextChoiceId)
        {
            return StartupStepRecordResult.Duplicate;
        }

        byPlayer[playerNetId] = incoming.NextChoiceId >= existing.NextChoiceId ? incoming : existing;
        return StartupStepRecordResult.Updated;
    }

    public bool HasPendingStartupStepCompletionMessageForEpoch(
        int syncEpoch,
        int stepIndex,
        ulong playerNetId)
    {
        return PendingStartupStepCompletionMessagesByEpoch.TryGetValue(syncEpoch, out Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>? byStep)
               && byStep.TryGetValue(stepIndex, out Dictionary<ulong, StartupStepCompletionInfo>? byPlayer)
               && byPlayer.ContainsKey(playerNetId);
    }

    public int GetPendingStartupStepCompletionMessageCountForEpoch(
        int syncEpoch,
        int stepIndex)
    {
        return PendingStartupStepCompletionMessagesByEpoch.TryGetValue(syncEpoch, out Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>? byStep)
               && byStep.TryGetValue(stepIndex, out Dictionary<ulong, StartupStepCompletionInfo>? byPlayer)
            ? byPlayer.Count
            : 0;
    }

    public IReadOnlyDictionary<ulong, StartupStepCompletionInfo> GetPendingStartupStepCompletionMessagesForEpoch(
        int syncEpoch,
        int stepIndex)
    {
        if (PendingStartupStepCompletionMessagesByEpoch.TryGetValue(syncEpoch, out Dictionary<int, Dictionary<ulong, StartupStepCompletionInfo>>? byStep)
            && byStep.TryGetValue(stepIndex, out Dictionary<ulong, StartupStepCompletionInfo>? byPlayer))
        {
            return byPlayer;
        }

        return new Dictionary<ulong, StartupStepCompletionInfo>();
    }

    public string DescribePendingStartupStepCompletionMessages()
    {
        if (PendingStartupStepCompletionMessagesByEpoch.Count == 0)
            return "<none>";

        return string.Join(
            " | ",
            PendingStartupStepCompletionMessagesByEpoch
                .OrderBy(epochEntry => epochEntry.Key)
                .Select(epochEntry =>
                    $"epoch {epochEntry.Key}: " +
                    string.Join(
                        "; ",
                        epochEntry.Value.OrderBy(stepEntry => stepEntry.Key).Select(stepEntry =>
                            $"step {stepEntry.Key}: " +
                            string.Join(
                                ", ",
                                stepEntry.Value.OrderBy(playerEntry => playerEntry.Key).Select(playerEntry =>
                                    $"{playerEntry.Key}->{playerEntry.Value.ModifierId}/{playerEntry.Value.NextChoiceId}/{playerEntry.Value.TotalStepCount}"))))));
    }


}
