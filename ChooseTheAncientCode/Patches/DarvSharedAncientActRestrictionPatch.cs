using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(ActModel), nameof(ActModel.SetSharedAncientSubset))]
public static class DarvSharedAncientActRestrictionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActModel __instance)
    {
        if (__instance.Index is 1 or 2)
            return;

        List<AncientEventModel>? sharedAncientSubset = Traverse.Create(__instance)
            .Field("_sharedAncientSubset")
            .GetValue<List<AncientEventModel>>();

        int removed = sharedAncientSubset?.RemoveAll(ChooseTheAncientHelpers.IsDarvAncient) ?? 0;
        if (removed > 0)
        {
            ModLog.Info(
                $"Removed Darv from shared Ancient generation for {__instance.Id.Entry} " +
                $"because act index {__instance.Index} is outside vanilla Act 2/3.");
        }
    }
}
