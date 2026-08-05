using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Registers the immediate console selection-resolution message handler after
/// RunManager receives the network service for a run.
/// </summary>
[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class ChooseTheAncientConsoleSelectionResolutionHandlerRegistrationPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ChooseTheAncientConsoleDebugState.EnsureSelectionResolutionHandlerRegistered();
    }
}
