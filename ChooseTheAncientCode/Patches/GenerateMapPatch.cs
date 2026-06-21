using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyGeneratedMapLate))]
public static class GenerateMapPatch
{
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(IRunState runState, ref ActMap __result, int actIndex)
    {
        if (runState is not RunState mutableRunState)
            return;

        if (actIndex != 0)
            return;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(mutableRunState);
        if (!ChooseTheAncientHelpers.ShouldUseAct1StartShell(mutableRunState, flow))
            return;

        if (__result.StartingMapPoint.PointType != MapPointType.Unknown)
        {
            __result.StartingMapPoint.PointType = MapPointType.Unknown;
            ModLog.Info("GenerateMapPatch converted the Act 1 starting map point into the ChooseTheAncient shell node before map display.");
        }
    }
}
