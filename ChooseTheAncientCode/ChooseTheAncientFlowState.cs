using System;
using System.Collections.Generic;
using System.Linq;

namespace ChooseTheAncient.ChooseTheAncientCode;


public enum StartupReadyRecordResult
{
    Added,
    Updated,
    Duplicate
}

public sealed class ChooseTheAncientFlowState
{
    public HashSet<int> ResolvedActs { get; } = new();
    public bool FlowInProgress { get; set; }
    public bool ContinueEnterMapCoord { get; set; }
    public bool ContinueEnterNextAct { get; set; }

    // This is a one-shot per-run flag for startup modifier bootstrap work
    // (for example Sealed Deck). Once it flips true, CTA must not try to
    // bootstrap startup modifiers again later in the Act 1 flow.
    public bool ModifierBootstrapCompleted { get; set; }

    // Compatibility alias for future patches / logs that want a more explicit name.
    public bool Act1StartupBootstrapApplied
    {
        get => ModifierBootstrapCompleted;
        set => ModifierBootstrapCompleted = value;
    }

    // Tracks the next CTA-owned choice id that should be consumed for a given
    // player after startup modifier bootstrap has already advanced the
    // global PlayerChoiceSynchronizer stream.
    public Dictionary<ulong, uint> StartupFlowNextChoiceIdsByPlayer { get; } = new();

    public bool HasStartupFlowTrackedChoiceIds => StartupFlowNextChoiceIdsByPlayer.Count > 0;

    // Explicit startup-step completion messages for bootstrap actions. These let CTA
    // synchronize per-modifier startup completion without inferring "done" from frame silence.
    public Dictionary<int, Dictionary<string, Dictionary<ulong, uint>>> PendingStartupStepCompletionMessagesByEpoch { get; } = new();

    // CTA-owned startup bootstrap sync epoch. This lets us ignore stale startup-step completion
    // messages from older attempts while keeping the sync off PlayerChoiceSynchronizer.
    public int Act1StartupBootstrapSyncEpoch { get; private set; }

    public int BeginAct1StartupBootstrapSyncEpoch()
    {
        Act1StartupBootstrapSyncEpoch++;
        return Act1StartupBootstrapSyncEpoch;
    }

    public void SetStartupFlowNextChoiceId(ulong netId, uint nextChoiceId)
    {
        if (StartupFlowNextChoiceIdsByPlayer.TryGetValue(netId, out uint existingNextChoiceId)
            && existingNextChoiceId >= nextChoiceId)
        {
            return;
        }

        StartupFlowNextChoiceIdsByPlayer[netId] = nextChoiceId;
    }

    public bool TryConsumeStartupFlowChoiceId(ulong netId, out uint choiceId)
    {
        if (StartupFlowNextChoiceIdsByPlayer.TryGetValue(netId, out choiceId))
        {
            StartupFlowNextChoiceIdsByPlayer[netId] = choiceId + 1;
            return true;
        }

        choiceId = 0;
        return false;
    }

    public void ClearStartupFlowChoiceIds()
    {
        StartupFlowNextChoiceIdsByPlayer.Clear();
    }

    public StartupReadyRecordResult RecordPendingStartupStepCompletionMessage(
        int bootstrapSyncEpoch,
        string stepKey,
        ulong netId,
        uint nextChoiceId)
    {
        if (!PendingStartupStepCompletionMessagesByEpoch.TryGetValue(
                bootstrapSyncEpoch,
                out Dictionary<string, Dictionary<ulong, uint>>? byStep))
        {
            byStep = new Dictionary<string, Dictionary<ulong, uint>>(StringComparer.Ordinal);
            PendingStartupStepCompletionMessagesByEpoch[bootstrapSyncEpoch] = byStep;
        }

        if (!byStep.TryGetValue(stepKey, out Dictionary<ulong, uint>? byPlayer))
        {
            byPlayer = new Dictionary<ulong, uint>();
            byStep[stepKey] = byPlayer;
        }

        StartupReadyRecordResult result;
        if (byPlayer.TryGetValue(netId, out uint existingNextChoiceId))
        {
            if (existingNextChoiceId == nextChoiceId || existingNextChoiceId > nextChoiceId)
            {
                result = StartupReadyRecordResult.Duplicate;
                nextChoiceId = existingNextChoiceId;
            }
            else
            {
                result = StartupReadyRecordResult.Updated;
            }
        }
        else
        {
            result = StartupReadyRecordResult.Added;
        }

        byPlayer[netId] = nextChoiceId;

        if (bootstrapSyncEpoch == Act1StartupBootstrapSyncEpoch)
        {
            SetStartupFlowNextChoiceId(netId, nextChoiceId);
        }

        return result;
    }

    public int ImportPendingStartupStepCompletionMessagesForCurrentSyncEpoch(string stepKey)
    {
        if (!PendingStartupStepCompletionMessagesByEpoch.TryGetValue(
                Act1StartupBootstrapSyncEpoch,
                out Dictionary<string, Dictionary<ulong, uint>>? byStep)
            || !byStep.TryGetValue(stepKey, out Dictionary<ulong, uint>? byPlayer)
            || byPlayer.Count == 0)
        {
            return 0;
        }

        int imported = 0;
        foreach (KeyValuePair<ulong, uint> entry in byPlayer)
        {
            SetStartupFlowNextChoiceId(entry.Key, entry.Value);
            imported++;
        }

        return imported;
    }

    public bool HasPendingStartupStepCompletionMessageForEpoch(int bootstrapSyncEpoch, string stepKey, ulong netId)
    {
        return PendingStartupStepCompletionMessagesByEpoch.TryGetValue(
                   bootstrapSyncEpoch,
                   out Dictionary<string, Dictionary<ulong, uint>>? byStep)
               && byStep.TryGetValue(stepKey, out Dictionary<ulong, uint>? byPlayer)
               && byPlayer.ContainsKey(netId);
    }

    public int GetPendingStartupStepCompletionMessageCountForEpoch(int bootstrapSyncEpoch, string stepKey)
    {
        return PendingStartupStepCompletionMessagesByEpoch.TryGetValue(
                   bootstrapSyncEpoch,
                   out Dictionary<string, Dictionary<ulong, uint>>? byStep)
               && byStep.TryGetValue(stepKey, out Dictionary<ulong, uint>? byPlayer)
            ? byPlayer.Count
            : 0;
    }

    public void ClearPendingStartupStepCompletionMessages()
    {
        PendingStartupStepCompletionMessagesByEpoch.Clear();
    }

    public string DescribePendingStartupStepCompletionMessages()
    {
        if (PendingStartupStepCompletionMessagesByEpoch.Count == 0)
            return "<none>";

        return string.Join(
            " | ",
            PendingStartupStepCompletionMessagesByEpoch
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                    $"epoch {kvp.Key}: " +
                    string.Join(
                        "; ",
                        kvp.Value
                            .OrderBy(step => step.Key, StringComparer.Ordinal)
                            .Select(step =>
                                $"{step.Key}=[" +
                                string.Join(", ", step.Value.OrderBy(player => player.Key).Select(player => $"{player.Key}->{player.Value}")) +
                                "]"))));
    }

    public string DescribeStartupFlowChoiceIds()
    {
        if (StartupFlowNextChoiceIdsByPlayer.Count == 0)
            return "<none>";

        return string.Join(
            ", ",
            StartupFlowNextChoiceIdsByPlayer
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}->{kvp.Value}"));
    }

    public bool Act1StartingRoomFlowTriggered { get; set; }
}
