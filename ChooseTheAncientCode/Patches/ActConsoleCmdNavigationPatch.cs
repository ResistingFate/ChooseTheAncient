using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Clears stale vanilla act-transition state after a successful
/// <c>act</c> console command.
/// </summary>
[HarmonyPatch(typeof(ActConsoleCmd), nameof(ActConsoleCmd.Process))]
public static class ActConsoleCmdNavigationPatch
{
    // _lastTransitioningActIndex was added after the sts2 0.107.1 main.
    private static readonly FieldInfo? LastTransitioningActIndexField =
        AccessTools.Field(typeof(ActChangeSynchronizer), "_lastTransitioningActIndex");

    private static readonly FieldInfo? ReadyPlayersField =
        AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers");

    private static bool _reportedMissingReadyPlayers;

    [HarmonyPrefix]
    private static bool Prefix(
        Player? issuingPlayer,
        string[] args,
        ref CmdResult __result)
    {
        if (args.Length != 1
            || !int.TryParse(args[0], out int oneBasedTargetAct))
        {
            return true;
        }

        RunState? runState = issuingPlayer?.RunState as RunState;
        if (runState == null
            || oneBasedTargetAct < 1
            || oneBasedTargetAct > runState.Acts.Count)
        {
            return true;
        }

        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        bool hasActiveSelection =
            flow.FlowInProgress
            || flow.ConsoleNavigationInProgress
            || ChooseTheAncientSelectionScreen.HasUnclosedConsoleSelectionScreen();

        if (!hasActiveSelection)
        {
            return true;
        }

        ModLog.Info(
            $"Intercepting vanilla act {oneBasedTargetAct} while a CTA ballot is active. " +
            "The current ballot will be canceled before EnterAct begins.");

        int targetActIndex = oneBasedTargetAct - 1;
        int requestId =
            ChooseTheAncientConsoleDebugState.BeginBallotRequest();

        Task task = CloseActiveSelectionAndEnterActAsync(
            runState,
            flow,
            requestId,
            targetActIndex);

        __result = new CmdResult(
            task,
            success: true,
            $"Navigated to act '{oneBasedTargetAct}'.");

        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(
        string[] args,
        CmdResult __result)
    {
        if (!__result.success
            || args.Length != 1
            || !int.TryParse(args[0], out _))
        {
            return;
        }

        ClearStaleTransitionState("numeric vanilla act command");
    }

    private static async Task CloseActiveSelectionAndEnterActAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int requestId,
        int targetActIndex)
    {
        bool released =
            await ChooseTheAncientConsoleBallotRunner.ReleasePreviousConsoleFlowAsync(
                runState,
                flow,
                requestId,
                "vanilla act command");

        if (!released)
        {
            return;
        }

        NMapScreen.Instance?.SetTravelEnabled(enabled: true);
        await RunManager.Instance.EnterAct(targetActIndex);
    }

    internal static void ClearStaleTransitionState(string source)
    {
        ActChangeSynchronizer synchronizer =
            RunManager.Instance.ActChangeSynchronizer;

        // Newer builds track the transitioning act explicitly. Older main does not
        // have this field, so there is nothing equivalent to clear there.
        LastTransitioningActIndexField?.SetValue(synchronizer, -1);

        if (ReadyPlayersField?.GetValue(synchronizer) is List<bool> readyPlayers)
        {
            for (int i = 0; i < readyPlayers.Count; i++)
            {
                readyPlayers[i] = false;
            }
        }
        else if (!_reportedMissingReadyPlayers)
        {
            _reportedMissingReadyPlayers = true;
            ModLog.Warn(
                "Could not locate/read ActChangeSynchronizer._readyPlayers. " +
                "CTA will skip that optional dev-console transition cleanup.");
        }

        ModLog.Info(
            $"Cleared available vanilla act-transition state after {source} navigation.");
    }
}
