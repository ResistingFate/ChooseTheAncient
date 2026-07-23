using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(RunManager), "CreateRoom")]
public static class CreateRoomPatch
{
    private static bool Prefix(
        RunManager __instance,
        RoomType roomType,
        MapPointType mapPointType,
        AbstractModel? model,
        ref AbstractRoom __result)
    {
        RunState? runState = ChooseTheAncientHelpers.GetRunState(__instance);
        if (runState == null)
            return true;

        if (mapPointType != MapPointType.Ancient)
            return true;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);
        if (!ChooseTheAncientHelpers.ShouldUseAct1StartShell(runState, flow))
            return true;

        MapCoord? currentCoord = runState.CurrentMapCoord;
        if (!currentCoord.HasValue || currentCoord.Value != runState.Map.StartingMapPoint.coord)
            return true;

        __result = new ChooseTheAncientStartRoom();
        ModLog.Info("CreateRoomPatch replaced the Act 1 starting map-point room with the ChooseTheAncient custom shell room before vanilla CreateRoom handled the starting shell node.");
        return false;
    }
}
