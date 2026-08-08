using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Allows CTA to replace the room object for the starting Ancient shell.
/// </summary>
[HarmonyPatch(typeof(RunManager), "CreateRoom")]
public static class CreateRoomPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(
        RunManager __instance,
        RoomType roomType,
        MapPointType mapPointType,
        AbstractModel? model,
        ref AbstractRoom __result)
    {
        if (roomType != RoomType.Event
            || mapPointType != MapPointType.Ancient
            || model != null)
        {
            return;
        }

        RunState? runState = ChooseTheAncientHelpers.GetRunState(__instance);
        if (runState == null)
            return;

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        if (!ChooseTheAncientHelpers.ShouldUseStartingAncientShell(runState, flow))
            return;

        MapCoord? currentCoord = runState.CurrentMapCoord;
        if (!currentCoord.HasValue
            || currentCoord.Value != runState.Map.StartingMapPoint.coord)
        {
            return;
        }

        if (__result is not EventRoom)
        {
            ModLog.Warn(
                $"CreateRoomPatch expected vanilla to create EventRoom for act " +
                $"{runState.CurrentActIndex + 1}'s unresolved starting Ancient, " +
                $"but received {__result.GetType().Name}. Leaving another mod's result unchanged.");
            return;
        }

        __result = new ChooseTheAncientStartRoom();

        ModLog.Info(
            $"CreateRoomPatch replaced vanilla's completed EventRoom result with CTA's " +
            $"act {runState.CurrentActIndex + 1} starting shell. Vanilla CreateRoom was not skipped.");
    }
}
