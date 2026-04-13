using System.Threading.Tasks;
using Godot;
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
        return Task.CompletedTask;
    }

    public override Task Exit(IRunState? runState)
    {
        return Task.CompletedTask;
    }

    public override Task Resume(AbstractRoom exitedRoom, IRunState? runState)
    {
        NRun.Instance?.SetCurrentRoom(new ChooseTheAncientStartRoomNode());
        return Task.CompletedTask;
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
