using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterNextAct))]
public static class EnterNextActPatch
{
    static bool Prefix(RunManager __instance, ref Task __result)
    {
        RunState? runState = AncientBanHelpers.GetRunState(__instance);
        if (runState == null)
        {
            return true;
        }

        int nextActIndex = runState.CurrentActIndex + 1;

        if (nextActIndex is not (1 or 2)) // only act 2 and act 3 have ancients currently
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
            __result = Task.CompletedTask;
            return false;
        }

        flow.FlowInProgress = true;
        __result = AncientBanCoordinator.RunAsync(__instance, runState, nextActIndex, flow);
        return false;
    }
}