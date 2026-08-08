using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Patches NMapScreen's local-selection and finalized-travel entry points to
/// guard CTA's resolved starting Ancient node.
/// </summary>
public static class NMapScreenStartingAncientPatch
{
    [HarmonyPatch(
        typeof(NMapScreen),
        nameof(NMapScreen.OnMapPointSelectedLocally))]
    private static class OnMapPointSelectedLocallyPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            NMapScreen __instance,
            NMapPoint point)
        {
            if (!IsResolvedCurrentStartingPoint(
                    point.Point.coord,
                    out RunState? runState))
            {
                return true;
            }

            if (runState != null)
            {
                ClearStaleVotes(__instance, runState);
                ModLog.Warn(
                    $"Ignored a second selection of Act " +
                    $"{runState.CurrentActIndex + 1}'s already-resolved starting " +
                    $"Ancient node at {point.Point.coord}.");
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(NMapScreen),
        nameof(NMapScreen.TravelToMapCoord))]
    private static class TravelToMapCoordPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            NMapScreen __instance,
            MapCoord coord,
            ref Task __result)
        {
            if (!IsResolvedCurrentStartingPoint(
                    coord,
                    out RunState? runState))
            {
                return true;
            }

            __instance.IsTraveling = false;
            __instance.SetTravelEnabled(enabled: true);
            if (runState != null)
            {
                ClearStaleVotes(__instance, runState);
                __result = Task.CompletedTask;

                ModLog.Warn(
                    $"Suppressed duplicate travel into Act " +
                    $"{runState.CurrentActIndex + 1}'s resolved starting Ancient " +
                    $"node at {coord}.");
            }

            return false;
        }
    }

    private static bool IsResolvedCurrentStartingPoint(
        MapCoord coord,
        out RunState? runState)
    {
        runState =
            ChooseTheAncientHelpers.GetRunState(RunManager.Instance);

        if (runState == null
            || runState.CurrentActIndex <= 0
            || coord != runState.Map.StartingMapPoint.coord
            || runState.CurrentMapCoord != coord)
        {
            return false;
        }

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        return ChooseTheAncientHelpers.IsStartingAncientResolved(
            runState,
            flow,
            runState.CurrentActIndex);
    }

    private static void ClearStaleVotes(
        NMapScreen mapScreen,
        RunState runState)
    {
        RunManager.Instance.MapSelectionSynchronizer
            .OnLocationChanged(runState.MapLocation);
        mapScreen.PlayerVoteDictionary.Clear();
        mapScreen.RefreshAllMapPointVotes();
    }
}
