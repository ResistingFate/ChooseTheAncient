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

        // EnterNextAct owns three different transitions since 0.109.0:
        // entering another indexed act, entering The Architect after the final
        // indexed act, and finishing the run from a victory room. Selection Screen
        // should only intercept the first case.
        bool hasIndexedNextAct = nextActIndex < runState.Acts.Count;
        if (!hasIndexedNextAct)
        {
            bool isVictoryRoom = runState.CurrentRoom?.IsVictoryRoom == true;
            string terminalTransition = isVictoryRoom
                ? "finish the run from a victory room"
                : "enter The Architect because no indexed next act exists";

            ModLog.Info(
                $"Skipping CTA before EnterNextAct; vanilla will {terminalTransition}. " +
                $"CurrentActIndex={runState.CurrentActIndex}, NextActIndex={nextActIndex}, " +
                $"Acts.Count={runState.Acts.Count}, CurrentRoomIsVictory={isVictoryRoom}.");
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
