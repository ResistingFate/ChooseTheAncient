using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Clears stale vanilla act-transition state after a successful
/// <c>act</c> console command.
/// </summary>
[HarmonyPatch(typeof(ActConsoleCmd), nameof(ActConsoleCmd.Process))]
public static class ActConsoleCmdNavigationPatch
{
    private static readonly FieldInfo LastTransitioningActIndexField =
        AccessTools.Field(typeof(ActChangeSynchronizer), "_lastTransitioningActIndex")
        ?? throw new InvalidOperationException(
            "Could not locate ActChangeSynchronizer._lastTransitioningActIndex.");

    private static readonly FieldInfo ReadyPlayersField =
        AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers")
        ?? throw new InvalidOperationException(
            "Could not locate ActChangeSynchronizer._readyPlayers.");

    [HarmonyPostfix]
    private static void Postfix(CmdResult __result)
    {
        if (!__result.success)
        {
            return;
        }

        ClearStaleTransitionState("vanilla act command");
    }

    internal static void ClearStaleTransitionState(string source)
    {
        ActChangeSynchronizer synchronizer =
            RunManager.Instance.ActChangeSynchronizer;

        LastTransitioningActIndexField.SetValue(synchronizer, -1);

        List<bool> readyPlayers =
            (List<bool>)ReadyPlayersField.GetValue(synchronizer)!;

        for (int i = 0; i < readyPlayers.Count; i++)
        {
            readyPlayers[i] = false;
        }

        ModLog.Info(
            $"Cleared vanilla act-transition state after {source} navigation.");
    }
}
