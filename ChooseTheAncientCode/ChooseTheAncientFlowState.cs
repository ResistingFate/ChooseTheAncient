using System.Collections.Generic;
using System.Linq;

namespace ChooseTheAncient.ChooseTheAncientCode;

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

    // Ready messages can arrive before the local peer reaches the barrier-registration
    // wait loop. Cache them by barrier epoch so the current flow can import them once
    // it begins waiting.
    public Dictionary<int, Dictionary<ulong, uint>> PendingStartupReadyMessagesByEpoch { get; } = new();

    // CTA-owned post-bootstrap barrier epoch. This lets us ignore stale ready
    // messages from older attempts while keeping the barrier off PlayerChoiceSynchronizer.
    public int Act1StartupReadyBarrierEpoch { get; private set; }

    public int BeginAct1StartupReadyBarrier()
    {
        Act1StartupReadyBarrierEpoch++;
        StartupFlowNextChoiceIdsByPlayer.Clear();
        ImportPendingStartupReadyMessagesForCurrentEpoch();
        return Act1StartupReadyBarrierEpoch;
    }

    public void RecordPendingStartupReadyMessage(int barrierEpoch, ulong netId, uint nextChoiceId)
    {
        if (!PendingStartupReadyMessagesByEpoch.TryGetValue(barrierEpoch, out Dictionary<ulong, uint>? byPlayer))
        {
            byPlayer = new Dictionary<ulong, uint>();
            PendingStartupReadyMessagesByEpoch[barrierEpoch] = byPlayer;
        }

        byPlayer[netId] = nextChoiceId;

        if (barrierEpoch == Act1StartupReadyBarrierEpoch)
        {
            StartupFlowNextChoiceIdsByPlayer[netId] = nextChoiceId;
        }
    }

    public int ImportPendingStartupReadyMessagesForCurrentEpoch()
    {
        if (!PendingStartupReadyMessagesByEpoch.TryGetValue(Act1StartupReadyBarrierEpoch, out Dictionary<ulong, uint>? byPlayer)
            || byPlayer.Count == 0)
        {
            return 0;
        }

        int imported = 0;
        foreach (KeyValuePair<ulong, uint> entry in byPlayer)
        {
            StartupFlowNextChoiceIdsByPlayer[entry.Key] = entry.Value;
            imported++;
        }

        return imported;
    }

    public void SetStartupFlowNextChoiceId(ulong netId, uint nextChoiceId)
    {
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

    public void ClearPendingStartupReadyMessages()
    {
        PendingStartupReadyMessagesByEpoch.Clear();
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

    public string DescribePendingStartupReadyMessages()
    {
        if (PendingStartupReadyMessagesByEpoch.Count == 0)
            return "<none>";

        return string.Join(
            " | ",
            PendingStartupReadyMessagesByEpoch
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                    $"epoch {kvp.Key}: " +
                    string.Join(", ", kvp.Value.OrderBy(inner => inner.Key).Select(inner => $"{inner.Key}->{inner.Value}"))));
    }

    public bool Act1StartingRoomFlowTriggered { get; set; }
}
