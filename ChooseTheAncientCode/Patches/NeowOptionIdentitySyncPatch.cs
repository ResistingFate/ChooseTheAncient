using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Messages;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

[HarmonyPatch]
public static class NeowOptionIdentitySyncPatch
{
    private sealed class PendingChoiceIdentity
    {
        /*
         * Stores one remote Neow choice in a stable form instead of indexes and other mods can reorder Neow's option list
         * differently for each player.
        */ 
        public string EventId { get; init; } = string.Empty;
        public string OptionIdentity { get; init; } = string.Empty;
        public uint RawOptionIndex { get; init; }
        public RunLocation Location { get; init; }
    }

    private sealed class SynchronizerState
    {
        /*
         * Queue EventSynchronizer messages for this patch with a specific message handler.
         */
        public required RunLocationTargetedMessageBuffer Buffer { get; init; }
        public required MessageHandlerDelegate<ChooseTheAncientNeowOptionIdentityChosenMessage> Handler { get; init; }
        public Dictionary<ulong, Queue<PendingChoiceIdentity>> PendingChoicesBySender { get; } = new();
    }

    private static readonly ConditionalWeakTable<EventSynchronizer, SynchronizerState> States = new();

    [HarmonyPatch(typeof(EventSynchronizer), MethodType.Constructor,
        typeof(RunLocationTargetedMessageBuffer),
        typeof(INetGameService),
        typeof(IPlayerCollection),
        typeof(ulong),
        typeof(uint))]
    [HarmonyPostfix]
    private static void EventSynchronizerConstructorPostfix(EventSynchronizer __instance)
    {
        /*
         * Purpose:
         * - grab the synchronizer's existing message buffer
         * - register our Neow identity message handler on that same buffer
         */
        RunLocationTargetedMessageBuffer? buffer = Traverse.Create(__instance)
            .Field("_messageBuffer")
            .GetValue<RunLocationTargetedMessageBuffer>();

        if (buffer == null)
            return;

        SynchronizerState state = States.GetValue(__instance, _ =>
        {
            MessageHandlerDelegate<ChooseTheAncientNeowOptionIdentityChosenMessage> handler =
                (message, senderId) => HandleIdentityMessage(__instance, message, senderId);

            return new SynchronizerState
            {
                Buffer = buffer,
                Handler = handler
            };
        });

        state.Buffer.RegisterMessageHandler(state.Handler);
    }

    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.Dispose))]
    [HarmonyPrefix]
    private static void EventSynchronizerDisposePrefix(EventSynchronizer __instance)
    {
        if (!States.TryGetValue(__instance, out SynchronizerState? state))
            return;

        state.Buffer.UnregisterMessageHandler(state.Handler);
        States.Remove(__instance);
    }

    [HarmonyPatch(typeof(EventSynchronizer), "BeginEvent")]
    [HarmonyPostfix]
    private static void BeginEventPostfix(EventSynchronizer __instance)
    {
        /*
         * cleanup after after the last event e.g custom modifier
         */
        if (!States.TryGetValue(__instance, out SynchronizerState? state))
            return;

        state.PendingChoicesBySender.Clear();
    }

    [HarmonyPatch(typeof(EventSynchronizer), "ChooseLocalOption")]
    [HarmonyPrefix]
    private static void ChooseLocalOptionPrefix(EventSynchronizer __instance, int index)
    {
        if (!TryBuildLocalIdentityMessage(__instance, index, out ChooseTheAncientNeowOptionIdentityChosenMessage message))
            return;

        Traverse.Create(__instance)
            .Field("_netService")
            .GetValue<INetGameService>()
            ?.SendMessage(message);

        ModLog.Info(
            $"Sent CTA-selected Neow option identity before raw option index sync. " +
            $"Index={message.optionIndex}, Identity={message.optionIdentity}, Location={message.location}.");
    }

    [HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
    [HarmonyPrefix]
    private static void ChooseOptionForEventPrefix(EventSynchronizer __instance, Player player, ref int optionIndex)
    {
        /*
         * Changes optionIndex to the localIndex during Neow Option if we previously recived a queued indentity 
         */
        if (!ShouldUseIdentitySyncForPlayer(__instance, player, out EventModel? eventForPlayer))
            return;

        ulong localPlayerId = Traverse.Create(__instance).Field("_localPlayerId").GetValue<ulong>();
        if (player.NetId == localPlayerId)
            return;

        if (!States.TryGetValue(__instance, out SynchronizerState? state))
            return;

        if (!TryTakePendingIdentity(state, player.NetId, out PendingChoiceIdentity? pendingIdentity))
            return;

        if (!string.Equals(GetEventId(eventForPlayer), pendingIdentity.EventId, StringComparison.OrdinalIgnoreCase))
            return;

        int matchedIndex = FindOptionIndexByIdentity(eventForPlayer.CurrentOptions, pendingIdentity.OptionIdentity);
        if (matchedIndex >= 0)
        {
            ModLog.Info(
                $"Remapped CTA-selected Neow remote choice by stable identity. " +
                $"Player={player.NetId}, RawIndex={optionIndex}, MatchedIndex={matchedIndex}, Identity={pendingIdentity.OptionIdentity}.");

            optionIndex = matchedIndex;
            return;
        }

        ModLog.Warn(
            $"Could not remap CTA-selected Neow remote choice by identity. " +
            $"Player={player.NetId}, RawIndex={optionIndex}, Identity={pendingIdentity.OptionIdentity}, " +
            $"Available={DescribeOptions(eventForPlayer.CurrentOptions)}. Falling back to raw index.");
    }

    private static void HandleIdentityMessage(
        EventSynchronizer synchronizer,
        ChooseTheAncientNeowOptionIdentityChosenMessage message,
        ulong senderId)
    {
        /*
         * The identity message and the raw index message are separate network messages
         * and can arrive independently This lets us pair them up safely
         */
        if (!string.Equals(message.eventId, "NEOW", StringComparison.OrdinalIgnoreCase))
            return;

        ulong localPlayerId = Traverse.Create(synchronizer).Field("_localPlayerId").GetValue<ulong>();
        if (senderId == localPlayerId)
            return;

        if (!States.TryGetValue(synchronizer, out SynchronizerState? state))
            return;

        if (!state.PendingChoicesBySender.TryGetValue(senderId, out Queue<PendingChoiceIdentity>? queue))
        {
            queue = new Queue<PendingChoiceIdentity>();
            state.PendingChoicesBySender[senderId] = queue;
        }

        queue.Enqueue(new PendingChoiceIdentity
        {
            EventId = message.eventId,
            OptionIdentity = message.optionIdentity,
            RawOptionIndex = message.optionIndex,
            Location = message.location
        });

        ModLog.Info(
            $"Queued CTA-selected Neow option identity sync from player {senderId}. " +
            $"Identity={message.optionIdentity}, RawIndex={message.optionIndex}, QueueDepth={queue.Count}.");
    }

    private static bool TryBuildLocalIdentityMessage(
        EventSynchronizer synchronizer,
        int index,
        out ChooseTheAncientNeowOptionIdentityChosenMessage message)
    {
        /*
         * For the ChooseOptionLocalPrefix to confirm identity sync should be active
         */
        message = default;

        EventModel localEvent = synchronizer.GetLocalEvent();
        if (!ShouldUseIdentitySync(localEvent))
            return false;

        if (index < 0 || index >= localEvent.CurrentOptions.Count)
            return false;

        RunLocationTargetedMessageBuffer? buffer = Traverse.Create(synchronizer)
            .Field("_messageBuffer")
            .GetValue<RunLocationTargetedMessageBuffer>();

        if (buffer == null)
            return false;

        EventOption option = localEvent.CurrentOptions[index];
        message = new ChooseTheAncientNeowOptionIdentityChosenMessage
        {
            eventId = GetEventId(localEvent),
            optionIdentity = BuildOptionIdentity(option),
            optionIndex = unchecked((uint)index),
            location = buffer.CurrentLocation
        };

        return !string.IsNullOrWhiteSpace(message.optionIdentity);
    }

    private static bool ShouldUseIdentitySyncForPlayer(
        EventSynchronizer synchronizer,
        Player player,
        out EventModel? eventForPlayer)
    {
        eventForPlayer = synchronizer.GetEventForPlayer(player);
        return ShouldUseIdentitySync(eventForPlayer);
    }

    private static bool ShouldUseIdentitySync(EventModel eventModel)
    {
        if (!string.Equals(GetEventId(eventModel), "NEOW", StringComparison.OrdinalIgnoreCase))
            return false;

        RunState? runState = eventModel.Owner?.RunState as RunState;
        if (runState == null)
            return false;

        return ChooseTheAncientStateStore.Get(runState).ForceNeowBlessingMode;
    }

    private static bool TryTakePendingIdentity(
        SynchronizerState state,
        ulong senderId,
        out PendingChoiceIdentity? pendingIdentity)
    {
        /*
         * For ChooseOptionForEventPrefix to pair one received identity message with one incoming raw index message
         */
        pendingIdentity = null;

        if (!state.PendingChoicesBySender.TryGetValue(senderId, out Queue<PendingChoiceIdentity>? queue))
            return false;

        if (queue.Count <= 0)
            return false;

        pendingIdentity = queue.Dequeue();
        if (queue.Count <= 0)
        {
            state.PendingChoicesBySender.Remove(senderId);
        }

        return true;
    }

    private static int FindOptionIndexByIdentity(IReadOnlyList<EventOption> options, string identity)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(BuildOptionIdentity(options[i]), identity, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string BuildOptionIdentity(EventOption? option)
    {
        /*
         * Exhaustive identity builder that gives the best chance that two peers can match the same logical option.
         */
        string textKey = option?.TextKey ?? string.Empty;
        string locTable = option?.Title?.LocTable ?? string.Empty;
        string locKey = option?.Title?.LocEntryKey ?? string.Empty;
        string relicEntry = option?.Relic?.Id?.Entry ?? option?.Relic?.GetType().FullName ?? string.Empty;
        string titleText = option?.Title?.GetFormattedText() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(textKey) && !string.IsNullOrWhiteSpace(relicEntry))
            return $"text:{textKey}|relic:{relicEntry}";

        if (!string.IsNullOrWhiteSpace(locKey) && !string.IsNullOrWhiteSpace(relicEntry))
            return $"loc:{locTable}.{locKey}|relic:{relicEntry}";

        if (!string.IsNullOrWhiteSpace(textKey))
            return $"text:{textKey}";

        if (!string.IsNullOrWhiteSpace(locKey))
            return $"loc:{locTable}.{locKey}";

        if (!string.IsNullOrWhiteSpace(relicEntry))
            return $"relic:{relicEntry}";

        if (!string.IsNullOrWhiteSpace(titleText))
            return $"title:{titleText}";

        return "<unidentified_option>";
    }

    private static string DescribeOptions(IReadOnlyList<EventOption> options)
    {
        List<string> described = new();
        for (int i = 0; i < options.Count; i++)
        {
            described.Add($"[{i}] {BuildOptionIdentity(options[i])}");
        }

        return string.Join(", ", described);
    }

    private static string GetEventId(EventModel eventModel)
    {
        return eventModel?.Id.Entry ?? eventModel?.GetType().Name ?? "<unknown_event>";
    }
}
