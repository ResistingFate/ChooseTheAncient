using System;
using System.Collections.Generic;
using HarmonyLib;
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
    [HarmonyPrefix]
    private static void InitialDescriptionPrefix(Neow __instance, out RunState? __state)
    {
        __state = TryBeginModifierMask(__instance);
    }

    [HarmonyPatch(typeof(Neow), "get_InitialDescription")]
    [HarmonyFinalizer]
    private static Exception? InitialDescriptionFinalizer(Exception? __exception, RunState? __state)
    {
        if (__state != null)
        {
            EndModifierMask(__state);
        }

        return __exception;
    }

    [HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
    [HarmonyPrefix]
    private static void GenerateInitialOptionsPrefix(Neow __instance, out RunState? __state)
    {
        __state = TryBeginModifierMask(__instance);
    }

    [HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
    [HarmonyFinalizer]
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
        if (!flow.ForceAct1NeowBlessingMode)
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

                ModLog.Info("Temporarily masked RunState.Modifiers so CTA-selected Neow uses blessing options instead of modifier rewards.");
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
            ModLog.Debug("Restored RunState.Modifiers after CTA-selected Neow finished building blessing content.");
        }
    }
}
