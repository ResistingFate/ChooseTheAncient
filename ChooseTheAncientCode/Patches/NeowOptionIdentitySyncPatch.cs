using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

    private sealed class EventSynchronizerTargets
    {
        public required MethodBase Constructor { get; init; }
        public required MethodInfo Dispose { get; init; }
        public required MethodInfo BeginEvent { get; init; }
        public required MethodInfo ChooseLocalOption { get; init; }
        public required MethodInfo ChooseOptionForEvent { get; init; }
        public required string ConstructorDescription { get; init; }
    }

    private static readonly ConditionalWeakTable<EventSynchronizer, SynchronizerState> States = new();

    private static bool _installed;

    public static void TryInstall(Harmony harmony)
    {
        /*
         * Compatibility:
         * - STS2 0.107.1 is the stable branch this mod supports.
         * - STS2 0.110.0 is the beta branch and uses IRunState plus a 64-bit seed.
         * - The transitional IRunState plus 32-bit seed signature remains a fallback.
         * - Future EventSynchronizer API changes do not prevent the rest
         *   of Choose The Ancient's Harmony patches from loading.
         */

        _installed = false;

        EventSynchronizerTargets? targets = FindEventSynchronizerTargets(out string missingTarget);
        if (targets == null)
        {
            ReportIdentitySyncUnavailable(
                $"CTA Neow multiplayer identity sync was not installed because the EventSynchronizer target " +
                $"'{missingTarget}' was not found. Single-player CTA and non-Neow CTA multiplayer should still work, " +
                "but CTA-selected Neow in multiplayer may fall back to raw option-index syncing.");
            return;
        }

        MethodInfo? constructorPostfix = AccessTools.Method(
            typeof(NeowOptionIdentitySyncPatch),
            nameof(EventSynchronizerConstructorPostfix));
        MethodInfo? disposePrefix = AccessTools.Method(
            typeof(NeowOptionIdentitySyncPatch),
            nameof(EventSynchronizerDisposePrefix));
        MethodInfo? beginEventPostfix = AccessTools.Method(
            typeof(NeowOptionIdentitySyncPatch),
            nameof(BeginEventPostfix));
        MethodInfo? chooseLocalOptionPrefix = AccessTools.Method(
            typeof(NeowOptionIdentitySyncPatch),
            nameof(ChooseLocalOptionPrefix));
        MethodInfo? chooseOptionForEventPrefix = AccessTools.Method(
            typeof(NeowOptionIdentitySyncPatch),
            nameof(ChooseOptionForEventPrefix));

        if (constructorPostfix == null ||
            disposePrefix == null ||
            beginEventPostfix == null ||
            chooseLocalOptionPrefix == null ||
            chooseOptionForEventPrefix == null)
        {
            ReportIdentitySyncUnavailable(
                "CTA Neow multiplayer identity sync was not installed because one of its local Harmony patch methods " +
                "could not be found. Single-player CTA and non-Neow CTA multiplayer should still work, but " +
                "CTA-selected Neow in multiplayer may fall back to raw option-index syncing.");
            return;
        }

        List<MethodBase> patchedTargets = new();

        try
        {
            harmony.Patch(targets.Constructor, postfix: new HarmonyMethod(constructorPostfix));
            patchedTargets.Add(targets.Constructor);

            harmony.Patch(targets.Dispose, prefix: new HarmonyMethod(disposePrefix));
            patchedTargets.Add(targets.Dispose);

            harmony.Patch(targets.BeginEvent, postfix: new HarmonyMethod(beginEventPostfix));
            patchedTargets.Add(targets.BeginEvent);

            harmony.Patch(targets.ChooseLocalOption, prefix: new HarmonyMethod(chooseLocalOptionPrefix));
            patchedTargets.Add(targets.ChooseLocalOption);

            harmony.Patch(targets.ChooseOptionForEvent, prefix: new HarmonyMethod(chooseOptionForEventPrefix));
            patchedTargets.Add(targets.ChooseOptionForEvent);

            _installed = true;

            ModLog.Info(
                $"EventSynchronizer identity sync patches installed. " +
                $"Constructor target: {targets.ConstructorDescription}.");
        }
        catch (Exception ex)
        {
            _installed = false;

            foreach (MethodBase patchedTarget in patchedTargets)
            {
                try
                {
                    harmony.Unpatch(patchedTarget, HarmonyPatchType.All, harmony.Id);
                }
                catch (Exception unpatchEx)
                {
                    ModLog.Warn(
                        $"Could not cleanly unpatch partial CTA Neow identity sync target {patchedTarget.Name}: " +
                        $"{unpatchEx.GetType().Name}: {unpatchEx.Message}");
                }
            }

            ReportIdentitySyncUnavailable(
                $"CTA Neow multiplayer identity sync was not installed because Harmony failed while applying the " +
                $"optional EventSynchronizer patches: {ex.GetType().Name}: {ex.Message}. Single-player CTA and " +
                "non-Neow CTA multiplayer should still work, but CTA-selected Neow in multiplayer may fall back to " +
                "raw option-index syncing.");
        }
    }

    private static EventSynchronizerTargets? FindEventSynchronizerTargets(out string missingTarget)
    {
        //missingTarget = "forced test failure";
        //return null;
        MethodBase? constructor = FindEventSynchronizerConstructor(out string constructorDescription);
        if (constructor == null)
        {
            missingTarget = "constructor";
            return null;
        }

        MethodInfo? dispose = AccessTools.Method(
            typeof(EventSynchronizer),
            nameof(EventSynchronizer.Dispose));
        if (dispose == null)
        {
            missingTarget = nameof(EventSynchronizer.Dispose);
            return null;
        }

        MethodInfo? beginEvent = AccessTools.Method(
            typeof(EventSynchronizer),
            nameof(EventSynchronizer.BeginEvent));
        if (beginEvent == null)
        {
            missingTarget = nameof(EventSynchronizer.BeginEvent);
            return null;
        }

        MethodInfo? chooseLocalOption = AccessTools.Method(
            typeof(EventSynchronizer),
            nameof(EventSynchronizer.ChooseLocalOption),
            new[] { typeof(int) });
        if (chooseLocalOption == null)
        {
            missingTarget = nameof(EventSynchronizer.ChooseLocalOption);
            return null;
        }

        MethodInfo? chooseOptionForEvent = AccessTools.Method(
            typeof(EventSynchronizer),
            "ChooseOptionForEvent",
            new[] { typeof(Player), typeof(int) });
        if (chooseOptionForEvent == null)
        {
            missingTarget = "ChooseOptionForEvent";
            return null;
        }

        missingTarget = string.Empty;
        return new EventSynchronizerTargets
        {
            Constructor = constructor,
            Dispose = dispose,
            BeginEvent = beginEvent,
            ChooseLocalOption = chooseLocalOption,
            ChooseOptionForEvent = chooseOptionForEvent,
            ConstructorDescription = constructorDescription
        };
    }

    // This is where we check which version of the game we're on by looking at EventSynchronizer and then
    // picking the right arguments for EventSynchronizer.
    private static MethodBase? FindEventSynchronizerConstructor(out string targetDescription)
    {
        Type? runStateInterface = typeof(EventSynchronizer).Assembly
            .GetType("MegaCrit.Sts2.Core.Runs.IRunState");

        if (runStateInterface != null)
        {
            MethodBase? betaConstructor = AccessTools.Constructor(typeof(EventSynchronizer), new[]
            {
                typeof(RunLocationTargetedMessageBuffer),
                typeof(INetGameService),
                typeof(IPlayerCollection),
                runStateInterface,
                typeof(ulong),
                typeof(ulong)
            });

            if (betaConstructor != null)
            {
                targetDescription = "0.110.0 beta signature with IRunState and 64-bit seed";
                return betaConstructor;
            }

            MethodBase? transitionalConstructor = AccessTools.Constructor(typeof(EventSynchronizer), new[]
            {
                typeof(RunLocationTargetedMessageBuffer),
                typeof(INetGameService),
                typeof(IPlayerCollection),
                runStateInterface,
                typeof(ulong),
                typeof(uint)
            });

            if (transitionalConstructor != null)
            {
                targetDescription = "transitional IRunState signature with 32-bit seed";
                return transitionalConstructor;
            }
        }

        MethodBase? stableConstructor = AccessTools.Constructor(typeof(EventSynchronizer), new[]
        {
            typeof(RunLocationTargetedMessageBuffer),
            typeof(INetGameService),
            typeof(IPlayerCollection),
            typeof(ulong),
            typeof(uint)
        });

        if (stableConstructor != null)
        {
            targetDescription = "0.107.1 stable signature without IRunState";
            return stableConstructor;
        }

        targetDescription = "not found";
        return null;
    }

    private static void ReportIdentitySyncUnavailable(string message)
    {
        ModLog.Error(message);
        ChooseTheAncientPlayerWarnings.ReportNeowMultiplayerIdentitySyncUnavailable();
    }

    private static void EventSynchronizerConstructorPostfix(EventSynchronizer __instance)
    {
        RegisterEventSynchronizerMessageHandler(__instance);
    }

    private static void RegisterEventSynchronizerMessageHandler(EventSynchronizer __instance)
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

        ModLog.Info("EventSynchronizer constructed; registered CTA Neow identity sync message handler.");
    }

    private static void EventSynchronizerDisposePrefix(EventSynchronizer __instance)
    {
        if (!States.TryGetValue(__instance, out SynchronizerState? state))
            return;

        state.Buffer.UnregisterMessageHandler(state.Handler);
        States.Remove(__instance);
    }

    private static void BeginEventPostfix(EventSynchronizer __instance)
    {
        /*
         * cleanup after after the last event e.g custom modifier
         */
        if (!States.TryGetValue(__instance, out SynchronizerState? state))
            return;

        state.PendingChoicesBySender.Clear();
    }

    private static void ChooseLocalOptionPrefix(EventSynchronizer __instance, int index)
    {
        if (!_installed)
            return;

        if (!States.TryGetValue(__instance, out _))
            return;

        if (!TryBuildLocalIdentityMessage(__instance, index, out ChooseTheAncientNeowOptionIdentityChosenMessage message))
            return;

        Traverse.Create(__instance)
            .Field("_netService")
            .GetValue<INetGameService>()
            ?.SendMessage(message);

        ModLog.Info(
            $"Sent CTA-selected Neow option identity before raw option index sync. " +
            $"Index={message.optionIndex}, Identity={message.OptionIdentityOrEmpty}, Location={message.location}.");
    }

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

        PendingChoiceIdentity? pendingIdentity = TakePendingIdentity(state, player.NetId);
        if (pendingIdentity == null)
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
        if (!string.Equals(message.EventIdOrEmpty, "NEOW", StringComparison.OrdinalIgnoreCase))
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
            EventId = message.EventIdOrEmpty,
            OptionIdentity = message.OptionIdentityOrEmpty,
            RawOptionIndex = message.optionIndex,
            Location = message.location
        });

        ModLog.Info(
            $"Queued CTA-selected Neow option identity sync from player {senderId}. " +
            $"Identity={message.OptionIdentityOrEmpty}, RawIndex={message.optionIndex}, QueueDepth={queue.Count}.");
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

        EventModel? localEvent = synchronizer.GetLocalEvent();
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
        message = ChooseTheAncientNeowOptionIdentityChosenMessage.Create(
            GetEventId(localEvent),
            BuildOptionIdentity(option),
            unchecked((uint)index),
            buffer.CurrentLocation);

        return !string.IsNullOrWhiteSpace(message.OptionIdentityOrEmpty);
    }

    private static bool ShouldUseIdentitySyncForPlayer(
        EventSynchronizer synchronizer,
        Player player,
        [NotNullWhen(true)] out EventModel? eventForPlayer)
    {
        eventForPlayer = synchronizer.GetEventForPlayer(player);
        return ShouldUseIdentitySync(eventForPlayer);
    }

    private static bool ShouldUseIdentitySync([NotNullWhen(true)] EventModel? eventModel)
    {
        if (eventModel == null)
            return false;

        if (!string.Equals(GetEventId(eventModel), "NEOW", StringComparison.OrdinalIgnoreCase))
            return false;

        RunState? runState = eventModel.Owner?.RunState as RunState;
        if (runState == null)
            return false;

        return ChooseTheAncientStateStore.Get(runState).ForceNeowBlessingMode;
    }

    private static PendingChoiceIdentity? TakePendingIdentity(SynchronizerState state, ulong senderId)
    {
        /*
         * For ChooseOptionForEventPrefix to pair one received identity message with one incoming raw index message.
         */
        if (!state.PendingChoicesBySender.TryGetValue(senderId, out Queue<PendingChoiceIdentity>? queue))
            return null;

        if (queue.Count <= 0)
            return null;

        PendingChoiceIdentity pendingIdentity = queue.Dequeue();
        if (queue.Count <= 0)
        {
            state.PendingChoicesBySender.Remove(senderId);
        }

        return pendingIdentity;
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

    private static string GetEventId(EventModel? eventModel)
    {
        return eventModel?.Id.Entry ?? eventModel?.GetType().Name ?? "<unknown_event>";
    }
}
