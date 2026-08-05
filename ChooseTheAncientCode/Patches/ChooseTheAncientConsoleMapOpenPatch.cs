using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Applies a deferred map-selection rebase when the map becomes interactive
/// after a console-entered Ancient room.
/// </summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class ChooseTheAncientConsoleMapOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMapScreen __instance)
    {
        RunState? runState =
            ChooseTheAncientConsoleDebugState.GetRunState(issuingPlayer: null);
        if (runState == null || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        ChooseTheAncientConsoleBallotRunner.TryApplyPendingMapSelectionRebase(
            runState,
            flow,
            "CTA console ballot map-open",
            __instance);
    }
}
