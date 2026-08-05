using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Cancels an older CTA ballot before vanilla starts a replacement ctaact or
/// ctastay command.
/// </summary>
[HarmonyPatch]
internal static class ChooseTheAncientConsoleBallotPreprocessPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(
                   typeof(DevConsole),
                   nameof(DevConsole.ProcessCommand),
                   [typeof(string)])
               ?? throw new InvalidOperationException(
                   "Could not locate DevConsole.ProcessCommand(string).");
    }

    [HarmonyPrefix]
    private static void Prefix(string inputValue)
    {
        string[] tokens = inputValue.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != 2
            || (!string.Equals(tokens[0], "ctaact", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tokens[0], "ctastay", StringComparison.OrdinalIgnoreCase))
            || !int.TryParse(tokens[1], out int targetActNumber))
        {
            return;
        }

        RunState? runState =
            ChooseTheAncientConsoleDebugState.GetRunState(issuingPlayer: null);
        if (runState == null
            || !RunManager.Instance.IsInProgress
            || targetActNumber < 1
            || targetActNumber > runState.Acts.Count)
        {
            return;
        }

        string commandName = tokens[0].ToLowerInvariant();

        if (ChooseTheAncientConsoleDebugState.TryResolveActiveSelectionForAllPeers(
                runState,
                ConsoleSelectionResolution.CancelFlow,
                $"superseding {commandName} command",
                out int activeTargetActIndex))
        {
            ModLog.Info(
                $"Canceled the active act {activeTargetActIndex + 1} CTA flow before " +
                $"queueing a replacement {commandName} command.");
        }

        if (NDevConsole.IsConsoleVisible)
        {
            NDevConsole.Instance.HideConsole();
        }
    }
}
