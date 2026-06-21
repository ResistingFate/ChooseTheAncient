using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
public static class AncientRewardRngPatch
{
    private static void Prefix(AncientEventModel __instance)
    /*
     * Adjusts the real ancient's reward RNG only when the ancient is appearing earlier or later than its normal minimum act.
     */
    {
        ChooseTheAncientHelpers.TryApplyCtaAncientRewardActOffsetRng(
            __instance,
            "AncientEventModel.GenerateInitialOptionsWrapper");
    }
}
