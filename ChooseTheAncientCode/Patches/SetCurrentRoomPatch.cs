using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch(typeof(NRun), nameof(NRun.SetCurrentRoom))]
public static class SetCurrentRoomPatch
{
    static void Postfix(Control? node)
    {
        if (node is not ChooseTheAncientStartRoomNode)
            return;

        RunState? runState = ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
        if (runState == null)
            return;

        if (!ChooseTheAncientHelpers.IsAct1StartingMapPoint(runState))
            return;

        if (runState.CurrentRoomCount != 1)
            return;

        if (runState.CurrentRoom is not ChooseTheAncientStartRoom)
            return;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);

        if (flow.ResolvedActs.Contains(0) || flow.FlowInProgress || flow.Act1StartingRoomFlowTriggered)
            return;

        flow.Act1StartingRoomFlowTriggered = true;
        flow.FlowInProgress = true;

        ModLog.Info(
            "SetCurrentRoomPatch detected the ChooseTheAncient Act 1 start shell room. " +
            $"Launching ChooseTheAncient from the entered room. RunSnapshot={ChooseTheAncientHelpers.DescribeRunLocation(runState)}");
        TaskHelper.RunSafely(ChooseTheAncientCoordinator.RunAct1StartingRoomFlowAsync(runState, flow));
    }
}

[HarmonyPatch(typeof(EventModel), "SetInitialEventState")]
public static class EventModelSetInitialEventStatePatch
{
    static void Postfix(EventModel __instance, bool isPreFinished)
    {
        if (__instance is not AncientEventModel)
            return;

        try
        {
            ModLog.Info(
                $"Ancient event initialized. Prefinished={isPreFinished}. " +
                $"{ChooseTheAncientHelpers.DescribeEventState(__instance)}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to log ancient event initialization for {__instance.Id.Entry}: {ex}");
        }
    }
}

[HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
public static class EventSynchronizerChooseOptionForEventPatch
{
    static void Prefix(EventSynchronizer __instance, Player player, int optionIndex)
    {
        try
        {
            EventModel eventForPlayer = __instance.GetEventForPlayer(player);
            if (eventForPlayer is not AncientEventModel)
                return;

            EventOption? option = optionIndex >= 0 && optionIndex < eventForPlayer.CurrentOptions.Count
                ? eventForPlayer.CurrentOptions[optionIndex]
                : null;

            ModLog.Info(
                "Ancient event option is about to resolve. " +
                $"Player={player.NetId}, RequestedIndex={optionIndex}, " +
                $"Option={ChooseTheAncientHelpers.DescribeOption(option, eventForPlayer, optionIndex)}, " +
                $"EventBeforeChoice={ChooseTheAncientHelpers.DescribeEventState(eventForPlayer)}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to log ancient event option resolution for player {player.NetId}: {ex}");
        }
    }
}

[HarmonyPatch(typeof(EventRoom), nameof(EventRoom.Exit))]
public static class EventRoomExitPatch
{
    static void Prefix(EventRoom __instance, IRunState? runState)
    {
        try
        {
            EventModel? localEvent = RunManager.Instance.EventSynchronizer?.GetLocalEvent();
            if (localEvent is not AncientEventModel)
                return;

            RunState? typedRunState = runState as RunState ?? ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
            ModLog.Info(
                "Ancient event room exiting. " +
                $"CanonicalEvent={__instance.CanonicalEvent.Id.Entry}, " +
                $"LocalEvent={ChooseTheAncientHelpers.DescribeEventState(localEvent)}, " +
                $"RunSnapshot={ChooseTheAncientHelpers.DescribeRunLocation(typedRunState)}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to log ancient event room exit for {__instance.CanonicalEvent.Id.Entry}: {ex}");
        }
    }
}
