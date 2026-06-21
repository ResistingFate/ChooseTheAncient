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
    private static readonly MethodInfo ShouldResetHpBeforeAncientHealMethod =
        AccessTools.Method(
            typeof(ChooseTheAncientHelpers),
            nameof(ChooseTheAncientHelpers.ShouldResetHpBeforeAncientHeal),
            [typeof(AncientEventModel)])
        ?? throw new InvalidOperationException("Could not locate ChooseTheAncientHelpers.ShouldResetHpBeforeAncientHeal.");

    private static MethodBase TargetMethod()
    /*
     * Targets AncientEventModel.BeforeEventStarted's real body, using the async state machine MoveNext when needed.
     */
    {
        MethodInfo original =
            AccessTools.Method(typeof(AncientEventModel), "BeforeEventStarted", [typeof(bool)])
            ?? throw new InvalidOperationException("Could not locate AncientEventModel.BeforeEventStarted(bool).");

        AsyncStateMachineAttribute? asyncStateMachine =
            original.GetCustomAttribute<AsyncStateMachineAttribute>();

        if (asyncStateMachine?.StateMachineType == null)
            return original;

        return AccessTools.Method(asyncStateMachine.StateMachineType, "MoveNext")
               ?? throw new InvalidOperationException(
                   $"Could not locate MoveNext for {asyncStateMachine.StateMachineType.FullName}.");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    /*
     * Replaces vanilla's "this is Neow" HP reset test with CTA's Act-1-start baseline test.
     */
    {
        List<CodeInstruction> codes = instructions.ToList();
        bool replaced = false;

        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction instruction = codes[i];

            if (replaced)
                continue;

            if (instruction.opcode != OpCodes.Isinst)
                continue;

            if (instruction.operand is not Type type)
                continue;

            if (type != typeof(Neow)
                && !string.Equals(type.FullName, typeof(Neow).FullName, StringComparison.Ordinal)
                && !string.Equals(type.Name, nameof(Neow), StringComparison.Ordinal))
            {
                continue;
            }

            instruction.opcode = OpCodes.Call;
            instruction.operand = ShouldResetHpBeforeAncientHealMethod;
            replaced = true;

            ModLog.Info(
                "Patched AncientEventModel.BeforeEventStarted Neow HP reset condition with CTA Act 1 baseline condition.");
        }

        if (!replaced)
        {
            ModLog.Warn(
                "Could not find vanilla Neow HP reset condition in AncientEventModel.BeforeEventStarted. " +
                "Act 1 non-Neow starts and later-act Neow healing may use vanilla HP behavior.");
        }

        return codes;
    }
}
