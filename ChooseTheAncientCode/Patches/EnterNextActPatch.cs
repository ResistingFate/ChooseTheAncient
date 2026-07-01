using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterNextAct))]
public static class EnterNextActPatch
{
    private static bool Prefix(RunManager __instance, ref Task __result)
    {
        RunState? runState = ChooseTheAncientHelpers.GetRunState(__instance);
        if (runState == null)
        {
            return true;
        }

        int nextActIndex = runState.CurrentActIndex + 1;
        if (nextActIndex < 0)
        {
            return true;
        }

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);

        if (flow.ContinueEnterNextAct)
        {
            flow.ContinueEnterNextAct = false;
            return true;
        }

        if (flow.FlowInProgress)
        {
            __result = Task.CompletedTask;
            return false;
        }

        flow.FlowInProgress = true;
        __result = ChooseTheAncientCoordinator.RunAsync(__instance, runState, nextActIndex, flow);
        return false;
    }
}
