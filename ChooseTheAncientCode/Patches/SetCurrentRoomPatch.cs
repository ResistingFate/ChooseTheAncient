using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(NRun), nameof(NRun.SetCurrentRoom))]
public static class SetCurrentRoomPatch
{
    private static void Postfix(Control? node)
    {
        if (node is not ChooseTheAncientStartRoomNode)
            return;

        RunState? runState = ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
        if (runState == null)
            return;

        if (!ChooseTheAncientHelpers.IsAct1StartingMapPoint(runState))
            return;

        if (runState.CurrentRoomCount != 1)
            return;

        if (runState.CurrentRoom is not ChooseTheAncientStartRoom)
            return;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);

        if (flow.ConsumeSuppressNextAct1StartingRoomFlow())
        {
            ModLog.Debug(
                "Suppressed the automatic Act 1 starting-room CTA flow because ctaact owns the requested ballot.");
            return;
        }

        if (flow.ResolvedActs.Contains(0) || flow.FlowInProgress || flow.Act1StartingRoomFlowTriggered)
            return;

        flow.Act1StartingRoomFlowTriggered = true;
        flow.FlowInProgress = true;

        ModLog.Info("SetCurrentRoomPatch detected the ChooseTheAncient Act 1 start shell room. Launching ChooseTheAncient from the entered room.");
        TaskHelper.RunSafely(ChooseTheAncientCoordinator.RunAct1StartingRoomFlowAsync(runState, flow));
    }
}
