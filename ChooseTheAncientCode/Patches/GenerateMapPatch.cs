using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyGeneratedMapLate))]
public static class GenerateMapPatch
{
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(
        IRunState runState,
        ref ActMap __result,
        int actIndex)
    {
        if (runState is not RunState mutableRunState)
            return;

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(mutableRunState);

        if (!ChooseTheAncientHelpers.ShouldPrepareUnresolvedStartingAncientNode(
                mutableRunState,
                flow,
                actIndex))
        {
            return;
        }

        if (__result.StartingMapPoint.PointType != MapPointType.Ancient)
        {
            __result.StartingMapPoint.PointType = MapPointType.Ancient;
            ModLog.Info(
                $"GenerateMapPatch converted act {actIndex + 1}'s starting map point " +
                "into CTA's unresolved Ancient node before map display.");
        }
    }
}
