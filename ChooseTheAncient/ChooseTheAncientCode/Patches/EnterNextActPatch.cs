using ChooseTheAncient.ChooseTheAncientCode;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterNextAct))]
public static class EnterNextActPatch
{
    static bool Prefix(RunManager __instance)
    {
        RunState? runState = AncientBanHelpers.GetRunState(__instance);
        if (runState == null)
        {
            return true;
        }

        int nextActIndex = runState.CurrentActIndex + 1;

        if (nextActIndex is not (1 or 2))
        {
            return true;
        }

        AncientBanFlowState flow = AncientBanStateStore.Get(runState);

        if (flow.ContinueEnterNextAct)
        {
            flow.ContinueEnterNextAct = false;
            return true;
        }

        if (flow.FlowInProgress || flow.ResolvedActs.Contains(nextActIndex))
        {
            return false;
        }

        flow.FlowInProgress = true;
        TaskHelper.RunSafely(AncientBanCoordinator.RunAsync(__instance, runState, nextActIndex, flow));
        return false;
    }
}