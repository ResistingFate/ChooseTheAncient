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
            /*
             that is fine for now, but ResolvedActs.Contains(nextActIndex) should probably only happen for weird reentry.
             If you ever see transitions stalling there, we can tighten that logic next.
             */
            __result = Task.CompletedTask;
            return false;
        }

        flow.FlowInProgress = true;
        __result = AncientBanCoordinator.RunAsync(__instance, runState, nextActIndex, flow);
        return false;
    }
}