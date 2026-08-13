using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch]
public static class NeowBlessingModePatch
{
    private sealed class ModifierMaskState
    {
        public int Depth { get; set; }
        public IReadOnlyList<ModifierModel>? OriginalModifiers { get; set; }
    }

    private static readonly object LockObj = new();
    private static readonly Dictionary<RunState, ModifierMaskState> ActiveMasks = new();

    [HarmonyPatch(typeof(Neow), "get_InitialDescription")]
    [HarmonyPostfix]
    private static void InitialDescriptionPostfix(Neow __instance, ref LocString __result)
    {
        RunState? runState = __instance.Owner?.RunState as RunState;
        if (runState == null ||
            !ChooseTheAncientStateStore.Get(runState).ForceNeowBlessingMode ||
            __instance.Owner == null)
        {
            return;
        }

        // Neow's override only changes the description when run modifiers are
        // present. Reproduce the inherited AncientEventModel result directly
        // instead of temporarily mutating RunState.Modifiers for a getter.
        __result = !RunManager.Instance.IsInProgress ||
                   Hook.ShouldAllowAncient(runState, __instance.Owner, __instance)
            ? new LocString(
                __instance.LocTable,
                $"{__instance.Id.Entry}.pages.INITIAL.description")
            : new LocString("relics", "WAX_CHOKER.blockMessage");
    }

    [HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void GenerateInitialOptionsPrefix(Neow __instance, out RunState? __state)
    {
        __state = TryBeginModifierMask(__instance);
    }



    [HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
    [HarmonyFinalizer]
    [HarmonyPriority(Priority.First)]
    private static Exception? GenerateInitialOptionsFinalizer(Exception? __exception, RunState? __state)
    {
        if (__state != null)
        {
            EndModifierMask(__state);
        }

        return __exception;
    }

    private static RunState? TryBeginModifierMask(Neow neow)
    {
        RunState? runState = neow.Owner?.RunState as RunState;
        if (runState == null)
            return null;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);
        if (!flow.ForceNeowBlessingMode)
            return null;

        lock (LockObj)
        {
            if (!ActiveMasks.TryGetValue(runState, out ModifierMaskState? state))
            {
                state = new ModifierMaskState
                {
                    Depth = 0,
                    OriginalModifiers = runState.Modifiers
                };
                ActiveMasks[runState] = state;
            }

            if (state.Depth == 0)
            {
                Traverse.Create(runState)
                    .Property<IReadOnlyList<ModifierModel>>(nameof(RunState.Modifiers))
                    .Value = Array.Empty<ModifierModel>();

                ModLog.Info("Temporarily masked RunState.Modifiers so CTA-selected Neow uses blessing options instead of modifier rewards regardless of act.");
            }

            state.Depth++;
        }

        return runState;
    }

    private static void EndModifierMask(RunState runState)
    {
        lock (LockObj)
        {
            if (!ActiveMasks.TryGetValue(runState, out ModifierMaskState? state))
                return;

            state.Depth--;
            if (state.Depth > 0)
                return;

            Traverse.Create(runState)
                .Property<IReadOnlyList<ModifierModel>>(nameof(RunState.Modifiers))
                .Value = state.OriginalModifiers ?? Array.Empty<ModifierModel>();

            ActiveMasks.Remove(runState);
            ModLog.Debug("Restored RunState.Modifiers immediately after CTA-selected Neow finished building blessing options.");
        }
    }
}
