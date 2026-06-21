using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch]
public static class AncientHpBaselineTranspilerPatch
{
    private static readonly MethodInfo? ShouldResetHpBeforeAncientHealMethod =
        AccessTools.Method(
            typeof(ChooseTheAncientHelpers),
            nameof(ChooseTheAncientHelpers.ShouldResetHpBeforeAncientHeal),
            [typeof(AncientEventModel)]);

    [HarmonyPrepare]
    private static bool Prepare()
    /*
     * Skips the patch cleanly if a future STS2 update moves or removes the async body this transpiler targets.
     */
    {
        if (ShouldResetHpBeforeAncientHealMethod == null)
        {
            ModLog.Warn(
                "CTA HP baseline transpiler was not applied because " +
                "ChooseTheAncientHelpers.ShouldResetHpBeforeAncientHeal(AncientEventModel) could not be found. " +
                "Act 1 CTA ancients or later-act CTA Neow healing may use vanilla HP behavior.");
            return false;
        }

        return ResolveTargetMethod(logWarnings: true) != null;
    }

    [HarmonyTargetMethod]
    private static MethodBase? TargetMethod()
    /*
     * Targets AncientEventModel.BeforeEventStarted's real body, using the async state machine MoveNext when needed.
     */
    {
        return ResolveTargetMethod(logWarnings: false);
    }

    private static MethodBase? ResolveTargetMethod(bool logWarnings)
    /*
     * Finds the method body that contains vanilla's Neow HP-to-zero reset.
     */
    {
        MethodInfo? original =
            AccessTools.Method(typeof(AncientEventModel), "BeforeEventStarted", [typeof(bool)]);

        if (original == null)
        {
            if (logWarnings)
            {
                ModLog.Warn(
                    "CTA HP baseline transpiler target was not found: " +
                    "AncientEventModel.BeforeEventStarted(bool) is missing. " +
                    "STS2 may have changed the ancient event start flow. " +
                    "Act 1 CTA ancients or later-act CTA Neow healing may use vanilla HP behavior.");
            }

            return null;
        }

        AsyncStateMachineAttribute? asyncStateMachine =
            original.GetCustomAttribute<AsyncStateMachineAttribute>();

        if (asyncStateMachine?.StateMachineType == null)
            return original;

        MethodInfo? moveNext = AccessTools.Method(asyncStateMachine.StateMachineType, "MoveNext");
        if (moveNext != null)
            return moveNext;

        if (logWarnings)
        {
            ModLog.Warn(
                "CTA HP baseline transpiler target was not found: " +
                $"MoveNext for {asyncStateMachine.StateMachineType.FullName} is missing. " +
                "STS2 may have changed AncientEventModel.BeforeEventStarted's async state machine. " +
                "Act 1 CTA ancients or later-act CTA Neow healing may use vanilla HP behavior.");
        }

        return null;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    /*
     * Replaces vanilla's "this is Neow" HP reset test with CTA's Act-1-start baseline test.
     */
    {
        List<CodeInstruction> codes = instructions.ToList();

        if (ShouldResetHpBeforeAncientHealMethod == null)
        {
            ModLog.Warn(
                "CTA HP baseline transpiler did not run because " +
                "ChooseTheAncientHelpers.ShouldResetHpBeforeAncientHeal(AncientEventModel) could not be found. " +
                "Returning unmodified IL.");
            return codes;
        }

        int replacements = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction instruction = codes[i];

            if (instruction.opcode != OpCodes.Isinst)
                continue;

            if (instruction.operand is not Type type)
                continue;

            if (!IsNeowType(type))
                continue;

            instruction.opcode = OpCodes.Call;
            instruction.operand = ShouldResetHpBeforeAncientHealMethod;
            replacements++;
        }

        if (replacements == 1)
        {
            ModLog.Info(
                "CTA HP baseline transpiler applied successfully. " +
                "Vanilla Neow HP reset condition now uses CTA's Act 1 starting-ancient baseline check.");
        }
        else if (replacements == 0)
        {
            ModLog.Warn(
                "CTA HP baseline transpiler did not find vanilla's Neow HP reset check in " +
                "AncientEventModel.BeforeEventStarted. STS2 may have changed the event start IL. " +
                "Expected symptom: Act 1 non-Neow CTA ancients may not start at the Weary Traveler HP baseline, " +
                "or later-act CTA Neow may still reset HP to zero before healing. " +
                "Review the decompiled BeforeEventStarted method and update AncientHpBaselineTranspilerPatch.");
        }
        else
        {
            ModLog.Warn(
                $"CTA HP baseline transpiler replaced {replacements} Neow type checks; expected exactly 1. " +
                "STS2 may have changed AncientEventModel.BeforeEventStarted, or this transpiler's match is too broad. " +
                "Review the decompiled method to confirm every replacement is still the HP reset condition.");
        }

        return codes;
    }

    private static bool IsNeowType(Type type)
    /*
     * Matches Neow defensively so the transpiler survives minor assembly identity differences in decompiled builds.
     */
    {
        return type == typeof(Neow)
               || string.Equals(type.FullName, typeof(Neow).FullName, StringComparison.Ordinal)
               || string.Equals(type.Name, nameof(Neow), StringComparison.Ordinal);
    }
}
