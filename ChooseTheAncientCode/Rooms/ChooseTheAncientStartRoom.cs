using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Rooms;

public sealed class ChooseTheAncientStartRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.Event;

    public override ModelId? ModelId => null;

    public override Task EnterInternal(IRunState? runState, bool isRestoringRoomStackBase)
    {
        if (isRestoringRoomStackBase)
            return Task.CompletedTask;

        NRun.Instance?.SetCurrentRoom(new ChooseTheAncientStartRoomNode());
        TryLaunchStartingRoomFlow(runState);
        return Task.CompletedTask;
    }

    public override Task Exit(IRunState? runState)
    {
        return Task.CompletedTask;
    }

    public override Task Resume(AbstractRoom exitedRoom, IRunState? runState)
    {
        NRun.Instance?.SetCurrentRoom(new ChooseTheAncientStartRoomNode());
        TryLaunchStartingRoomFlow(runState);
        return Task.CompletedTask;
    }

    private static void TryLaunchStartingRoomFlow(IRunState? runState)
    {
        if (runState is not RunState mutableRunState)
            return;

        int actIndex = mutableRunState.CurrentActIndex;

        if (!ChooseTheAncientHelpers.IsCurrentActStartingMapPoint(mutableRunState))
            return;

        if (mutableRunState.CurrentRoomCount != 1)
            return;

        if (mutableRunState.CurrentRoom is not ChooseTheAncientStartRoom)
            return;

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(mutableRunState);

        if (actIndex == 0 && flow.ConsumeSuppressNextAct1StartingRoomFlow())
        {
            ModLog.Debug(
                "Suppressed the automatic Act 1 starting-room CTA flow because ctaact owns the requested ballot.");
            return;
        }

        if (flow.ResolvedActs.Contains(actIndex)
            || flow.FlowInProgress
            || flow.StartingRoomFlowTriggeredActs.Contains(actIndex))
        {
            return;
        }

        flow.StartingRoomFlowTriggeredActs.Add(actIndex);
        flow.ActiveFlowTargetActIndex = actIndex;
        flow.FlowInProgress = true;

        if (actIndex == 0)
        {
            flow.Act1StartingRoomFlowTriggered = true;
            ModLog.Info(
                "ChooseTheAncientStartRoom entered the Act 1 shell. " +
                "Launching the room-owned Act 1 ballot without patching NRun.SetCurrentRoom.");

            TaskHelper.RunSafely(
                ChooseTheAncientCoordinator.RunAct1StartingRoomFlowAsync(
                    mutableRunState,
                    flow));
            return;
        }

        ModLog.Info(
            $"ChooseTheAncientStartRoom entered the act {actIndex + 1} shell. " +
            "Launching the room-owned ballot without patching NRun.SetCurrentRoom.");

        TaskHelper.RunSafely(
            ChooseTheAncientCoordinator.RunLaterActStartingRoomFlowAsync(
                mutableRunState,
                flow,
                actIndex));
    }

    public override SerializableRoom ToSerializable()
    {
        return new SerializableRoom
        {
            RoomType = RoomType.Map
        };
    }
}

public sealed partial class ChooseTheAncientStartRoomNode : Control, IScreenContext
{
    public Control? DefaultFocusedControl => null;

    public ChooseTheAncientStartRoomNode()
    {
        Name = "ChooseTheAncientStartRoomNode";
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = true;
        Modulate = Colors.Transparent;
    }
}
