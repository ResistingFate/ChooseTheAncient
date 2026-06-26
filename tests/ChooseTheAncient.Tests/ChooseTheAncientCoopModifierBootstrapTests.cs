using System;
using System.Collections.Generic;
using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode;
using Xunit;

namespace ChooseTheAncient.Tests;

public sealed class ChooseTheAncientCoopModifierBootstrapTests
{
    private const string Draft = "DRAFT";
    private const string SealedDeck = "SEALED_DECK";
    private const string Insanity = "INSANITY";
    private const string Specialized = "SPECIALIZED";
    private const string AllStar = "ALL_STAR";

    private const uint RunSeed = 123456789;
    private const ulong HostPlayerId = 1;
    private const ulong ClientPlayerId = 1000;

    private static readonly string[] MutuallyExclusiveDeckStartModifiers =
    [
        Draft,
        SealedDeck,
        Insanity
    ];

    private static readonly string[] CardRewardModifiers =
    [
        Specialized,
        AllStar
    ];

    public static IEnumerable<object[]> ValidAct1CardModifierCombinations()
    {
        string[] candidates = MutuallyExclusiveDeckStartModifiers
            .Concat(CardRewardModifiers)
            .ToArray();

        for (int size = 1; size <= 3; size++)
        {
            foreach (string[] combination in Combinations(candidates, size))
            {
                if (combination.Count(MutuallyExclusiveDeckStartModifiers.Contains) > 1)
                    continue;

                yield return new object[] { combination };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValidAct1CardModifierCombinations))]
    public void Coop_act1_modifier_bootstrap_and_per_player_neow_blessings_do_not_diverge(
        string[] modifierIds)
    {
        /*
         * This models two peers, each holding a replica of both players.
         *
         * The assertion is NOT that both players receive the same Neow blessing. In co-op they may receive different
         * blessings because Neow's event RNG is per-player. The assertion is:
         *
         *   host's replica of player A == client's replica of player A
         *   host's replica of player B == client's replica of player B
         *
         * That is the multiplayer invariant CTA must preserve.
         */
        IReadOnlyList<string> orderedModifiers = OrderLikeCoordinator(modifierIds);

        CoopPeer host = CoopPeer.CreateHost(RunSeed);
        CoopPeer client = CoopPeer.CreateClient(RunSeed);

        int hostEpoch = host.Flow.BeginAct1StartupBootstrapSyncEpoch();
        int clientEpoch = client.Flow.BeginAct1StartupBootstrapSyncEpoch();
        Assert.Equal(hostEpoch, clientEpoch);

        RunModifierBootstrapForLocalPlayerAndReplicate(
            orderedModifiers,
            host,
            client,
            hostEpoch,
            HostPlayerId);

        RunModifierBootstrapForLocalPlayerAndReplicate(
            orderedModifiers,
            client,
            host,
            clientEpoch,
            ClientPlayerId);

        host.MarkModifierBootstrapCompleted();
        client.MarkModifierBootstrapCompleted();

        host.ChooseAct1Ancient("NEOW");
        client.ChooseAct1Ancient("NEOW");

        Assert.True(host.Flow.ForceNeowBlessingMode);
        Assert.True(client.Flow.ForceNeowBlessingMode);
        Assert.True(host.Flow.ForceAct1NeowBlessingMode);
        Assert.True(client.Flow.ForceAct1NeowBlessingMode);

        Dictionary<ulong, IReadOnlyList<string>> hostNeowOptions = host.GenerateNeowOptionsForAllPlayers();
        Dictionary<ulong, IReadOnlyList<string>> clientNeowOptions = client.GenerateNeowOptionsForAllPlayers();

        foreach (ulong playerId in host.PlayerIds)
        {
            Assert.Equal(3, hostNeowOptions[playerId].Count);
            Assert.Equal(3, clientNeowOptions[playerId].Count);

            // Same player, same seed/slot/counter, same options on both peers.
            Assert.Equal(hostNeowOptions[playerId], clientNeowOptions[playerId]);
        }

        string hostPlayerBlessing = hostNeowOptions[HostPlayerId][0];
        string clientPlayerBlessing = hostNeowOptions[ClientPlayerId][1];

        // The players are allowed to receive different blessings. Force the test data to exercise that case when possible.
        if (string.Equals(hostPlayerBlessing, clientPlayerBlessing, StringComparison.Ordinal))
        {
            clientPlayerBlessing = hostNeowOptions[ClientPlayerId]
                .First(option => !string.Equals(option, hostPlayerBlessing, StringComparison.Ordinal));
        }

        Assert.NotEqual(hostPlayerBlessing, clientPlayerBlessing);

        ApplyNeowIdentityMessageToBothPeers(host, client, HostPlayerId, hostPlayerBlessing);
        ApplyNeowIdentityMessageToBothPeers(host, client, ClientPlayerId, clientPlayerBlessing);

        Assert.Equal(host.StateSignatureForPlayer(HostPlayerId), client.StateSignatureForPlayer(HostPlayerId));
        Assert.Equal(host.StateSignatureForPlayer(ClientPlayerId), client.StateSignatureForPlayer(ClientPlayerId));

        // Do not compare player A to player B. Different players can have different Neow results.
        Assert.NotEqual(host.StateSignatureForPlayer(HostPlayerId), host.StateSignatureForPlayer(ClientPlayerId));
    }

    [Fact]
    public void Coop_identity_sync_prevents_raw_index_divergence_when_peers_have_same_options_in_different_order()
    {
        CoopPeer host = CoopPeer.CreateHost(RunSeed);
        CoopPeer client = CoopPeer.CreateClient(RunSeed);

        host.MarkModifierBootstrapCompleted();
        client.MarkModifierBootstrapCompleted();
        host.ChooseAct1Ancient("NEOW");
        client.ChooseAct1Ancient("NEOW");

        IReadOnlyList<string> hostOptions = host.GenerateNeowOptionsForPlayer(HostPlayerId);
        IReadOnlyList<string> clientOptions = hostOptions.Reverse().ToArray();

        int rawIndexChosenOnHost = 0;
        string identityChosenOnHost = hostOptions[rawIndexChosenOnHost];

        int rawIndexOnClientWouldChoose = rawIndexChosenOnHost;
        Assert.NotEqual(identityChosenOnHost, clientOptions[rawIndexOnClientWouldChoose]);

        int remappedClientIndex = FindOptionIndexByIdentity(clientOptions, identityChosenOnHost);
        Assert.True(remappedClientIndex >= 0);

        host.ApplyNeowBlessingByIdentity(HostPlayerId, identityChosenOnHost);
        client.ApplyNeowBlessingByIdentity(HostPlayerId, clientOptions[remappedClientIndex]);

        Assert.Equal(host.StateSignatureForPlayer(HostPlayerId), client.StateSignatureForPlayer(HostPlayerId));
    }

    [Fact]
    public void Coop_simulation_catches_state_divergence_if_same_players_raw_neow_index_resolves_differently()
    {
        CoopPeer host = CoopPeer.CreateHost(RunSeed);
        CoopPeer client = CoopPeer.CreateClient(RunSeed);

        host.MarkModifierBootstrapCompleted();
        client.MarkModifierBootstrapCompleted();
        host.ChooseAct1Ancient("NEOW");
        client.ChooseAct1Ancient("NEOW");

        IReadOnlyList<string> hostOptions = host.GenerateNeowOptionsForPlayer(HostPlayerId);
        IReadOnlyList<string> clientOptions = hostOptions.Reverse().ToArray();

        int rawIndex = 0;
        Assert.NotEqual(hostOptions[rawIndex], clientOptions[rawIndex]);

        host.ApplyNeowBlessingByIdentity(HostPlayerId, hostOptions[rawIndex]);
        client.ApplyNeowBlessingByIdentity(HostPlayerId, clientOptions[rawIndex]);

        Assert.NotEqual(host.StateSignatureForPlayer(HostPlayerId), client.StateSignatureForPlayer(HostPlayerId));
    }

    [Fact]
    public void Coop_simulation_catches_state_divergence_if_same_players_neow_rng_counter_is_not_aligned()
    {
        CoopPeer host = CoopPeer.CreateHost(RunSeed);
        CoopPeer client = CoopPeer.CreateClient(RunSeed);

        IReadOnlyList<string> hostOptions = host.GenerateNeowOptionsForPlayer(HostPlayerId, extraCounterAdvance: 0);
        IReadOnlyList<string> clientOptions = client.GenerateNeowOptionsForPlayer(HostPlayerId, extraCounterAdvance: 1);

        Assert.NotEqual(hostOptions, clientOptions);
    }

    private static void RunModifierBootstrapForLocalPlayerAndReplicate(
        IReadOnlyList<string> orderedModifiers,
        CoopPeer localPeer,
        CoopPeer remotePeer,
        int syncEpoch,
        ulong actingPlayerId)
    {
        for (int stepIndex = 0; stepIndex < orderedModifiers.Count; stepIndex++)
        {
            string modifierId = orderedModifiers[stepIndex];

            localPeer.ApplyModifierBootstrapStep(actingPlayerId, modifierId);
            remotePeer.ApplyModifierBootstrapStep(actingPlayerId, modifierId);

            StartupStepMessage message = localPeer.CreateStartupStepMessage(
                actingPlayerId,
                syncEpoch,
                stepIndex,
                orderedModifiers.Count,
                modifierId);

            localPeer.RecordStartupStepMessage(message);
            remotePeer.RecordStartupStepMessage(message);

            Assert.True(localPeer.HasStartupStepMessage(syncEpoch, stepIndex, actingPlayerId));
            Assert.True(remotePeer.HasStartupStepMessage(syncEpoch, stepIndex, actingPlayerId));

            Assert.Equal(
                localPeer.StateSignatureForPlayer(actingPlayerId),
                remotePeer.StateSignatureForPlayer(actingPlayerId));
        }
    }

    private static void ApplyNeowIdentityMessageToBothPeers(
        CoopPeer host,
        CoopPeer client,
        ulong playerId,
        string chosenIdentity)
    {
        IReadOnlyList<string> hostOptions = host.GenerateNeowOptionsForPlayer(playerId);
        IReadOnlyList<string> clientOptions = client.GenerateNeowOptionsForPlayer(playerId);

        int hostIndex = FindOptionIndexByIdentity(hostOptions, chosenIdentity);
        int clientIndex = FindOptionIndexByIdentity(clientOptions, chosenIdentity);

        Assert.True(hostIndex >= 0);
        Assert.True(clientIndex >= 0);

        host.ApplyNeowBlessingByIdentity(playerId, hostOptions[hostIndex]);
        client.ApplyNeowBlessingByIdentity(playerId, clientOptions[clientIndex]);
    }

    private static int FindOptionIndexByIdentity(IReadOnlyList<string> options, string identity)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], identity, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static IReadOnlyList<string> OrderLikeCoordinator(IEnumerable<string> modifierIds)
    {
        List<ModifierBootstrapSpec> ordered = modifierIds
            .Select((modifierId, index) => new ModifierBootstrapSpec(modifierId, index))
            .OrderBy(action => action.RunModifierIndex)
            .ToList();

        MoveModifierBeforeIfBothPresent(ordered, SealedDeck, Draft);

        return ordered.Select(action => action.ModifierId).ToList();
    }

    private static void MoveModifierBeforeIfBothPresent(
        List<ModifierBootstrapSpec> ordered,
        string modifierToMoveId,
        string targetModifierId)
    {
        int moverIndex = ordered.FindIndex(action =>
            string.Equals(action.ModifierId, modifierToMoveId, StringComparison.OrdinalIgnoreCase));
        int targetIndex = ordered.FindIndex(action =>
            string.Equals(action.ModifierId, targetModifierId, StringComparison.OrdinalIgnoreCase));

        if (moverIndex < 0 || targetIndex < 0 || moverIndex < targetIndex)
            return;

        ModifierBootstrapSpec mover = ordered[moverIndex];
        ordered.RemoveAt(moverIndex);

        targetIndex = ordered.FindIndex(action =>
            string.Equals(action.ModifierId, targetModifierId, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            ordered.Add(mover);
            return;
        }

        ordered.Insert(targetIndex, mover);
    }

    private static IEnumerable<string[]> Combinations(string[] values, int size)
    {
        string[] buffer = new string[size];

        IEnumerable<string[]> Recurse(int start, int depth)
        {
            if (depth == size)
            {
                yield return buffer.ToArray();
                yield break;
            }

            for (int i = start; i <= values.Length - (size - depth); i++)
            {
                buffer[depth] = values[i];
                foreach (string[] combination in Recurse(i + 1, depth + 1))
                {
                    yield return combination;
                }
            }
        }

        return Recurse(0, 0);
    }

    private sealed class CoopPeer
    {
        private static readonly IReadOnlyList<string> NeowBlessingPool =
        [
            "NEOW.pages.INITIAL.options.SILKEN_TRESS",
            "NEOW.pages.INITIAL.options.GOLD",
            "NEOW.pages.INITIAL.options.MAX_HP",
            "NEOW.pages.INITIAL.options.LAVA_ROCK",
            "NEOW.pages.INITIAL.options.SMALL_CAPSULE",
            "NEOW.pages.INITIAL.options.NEOWS_TALISMAN",
            "NEOW.pages.INITIAL.options.POMANDER",
            "NEOW.pages.INITIAL.options.NUTRITIOUS_OYSTER",
            "NEOW.pages.INITIAL.options.STONE_HUMIDIFIER"
        ];

        private readonly Dictionary<ulong, PlayerReplica> _players;

        private CoopPeer(string name, ulong localPlayerId, uint runSeed)
        {
            Name = name;
            LocalPlayerId = localPlayerId;
            RunSeed = runSeed;

            _players = new Dictionary<ulong, PlayerReplica>
            {
                [HostPlayerId] = new(HostPlayerId, slotIndex: 0),
                [ClientPlayerId] = new(ClientPlayerId, slotIndex: 1)
            };
        }

        public string Name { get; }
        public ulong LocalPlayerId { get; }
        public uint RunSeed { get; }
        public ChooseTheAncientFlowState Flow { get; } = new();
        public IReadOnlyList<ulong> PlayerIds => _players.Keys.OrderBy(id => id).ToArray();

        public static CoopPeer CreateHost(uint runSeed) => new("host", HostPlayerId, runSeed);

        public static CoopPeer CreateClient(uint runSeed) => new("client", ClientPlayerId, runSeed);

        public void ApplyModifierBootstrapStep(ulong playerId, string modifierId)
        {
            _players[playerId].ApplyModifierBootstrapStep(modifierId);
        }

        public StartupStepMessage CreateStartupStepMessage(
            ulong actingPlayerId,
            int syncEpoch,
            int stepIndex,
            int totalStepCount,
            string modifierId)
        {
            return new StartupStepMessage(
                SenderNetId: actingPlayerId,
                SyncEpoch: syncEpoch,
                StepIndex: stepIndex,
                TotalStepCount: totalStepCount,
                ModifierId: modifierId,
                NextChoiceId: _players[actingPlayerId].NextChoiceId);
        }

        public void RecordStartupStepMessage(StartupStepMessage message)
        {
            Flow.RecordPendingStartupStepCompletionMessage(
                message.SyncEpoch,
                message.StepIndex,
                message.SenderNetId,
                message.TotalStepCount,
                message.ModifierId,
                message.NextChoiceId);
        }

        public bool HasStartupStepMessage(
            int syncEpoch,
            int stepIndex,
            ulong playerId)
        {
            return Flow.HasPendingStartupStepCompletionMessageForEpoch(syncEpoch, stepIndex, playerId);
        }

        public void MarkModifierBootstrapCompleted()
        {
            Flow.ModifierBootstrapCompleted = true;
        }

        public void ChooseAct1Ancient(string ancientId)
        {
            if (string.Equals(ancientId, "NEOW", StringComparison.OrdinalIgnoreCase))
            {
                Flow.ForceNeowBlessingMode = true;
            }
        }

        public Dictionary<ulong, IReadOnlyList<string>> GenerateNeowOptionsForAllPlayers()
        {
            return _players.Keys.ToDictionary(
                playerId => playerId,
                playerId => GenerateNeowOptionsForPlayer(playerId));
        }

        public IReadOnlyList<string> GenerateNeowOptionsForPlayer(
            ulong playerId,
            int extraCounterAdvance = 0)
        {
            PlayerReplica player = _players[playerId];

            /*
             * Mirrors EventModel.BeginEvent's seed shape:
             * run seed + player slot for non-shared events + deterministic event-id hash.
             * The exact hash/random algorithm is not reimplemented here; this test only needs the multiplayer property:
             * same player/slot/seed/counter gives the same option identities on both peers, while different players can
             * produce different options.
             */
            DeterministicRng rng = new(
                unchecked((uint)((int)RunSeed + player.SlotIndex + DeterministicHash("NEOW"))),
                player.NeowEventRngCounter + extraCounterAdvance);

            List<string> options = NeowBlessingPool.ToList();
            Shuffle(options, rng);

            return options.Take(3).ToArray();
        }

        public void ApplyNeowBlessingByIdentity(ulong playerId, string optionIdentity)
        {
            if (!Flow.ModifierBootstrapCompleted)
                throw new InvalidOperationException($"{Name} tried to apply Neow blessing before modifier bootstrap completed.");

            if (!Flow.ForceNeowBlessingMode)
                throw new InvalidOperationException($"{Name} tried to apply Neow blessing while ForceNeowBlessingMode was disabled.");

            _players[playerId].ApplyNeowBlessingByIdentity(optionIdentity);
        }

        public string StateSignatureForPlayer(ulong playerId)
        {
            return _players[playerId].StateSignature();
        }

        private static void Shuffle<T>(IList<T> values, DeterministicRng rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private static int DeterministicHash(string value)
        {
            unchecked
            {
                int hash = 5381;
                foreach (char c in value)
                {
                    hash = ((hash << 5) + hash) ^ c;
                }

                return hash;
            }
        }
    }

    private sealed class PlayerReplica
    {
        private readonly List<string> _deck = ["Strike", "Strike", "Strike", "Strike", "Defend", "Defend", "Defend", "Defend"];
        private readonly List<string> _relics = ["Burning Blood"];

        private int _gold;

        public PlayerReplica(ulong playerId, int slotIndex)
        {
            PlayerId = playerId;
            SlotIndex = slotIndex;
        }

        public ulong PlayerId { get; }
        public int SlotIndex { get; }
        public uint NextChoiceId { get; private set; } = 1;
        public int NeowEventRngCounter { get; private set; }

        public void ApplyModifierBootstrapStep(string modifierId)
        {
            switch (modifierId)
            {
                case Draft:
                    ReplaceStarterDeck("Drafted Attack", "Drafted Skill", "Drafted Power", "Drafted Attack", "Drafted Skill");
                    NextChoiceId += 5;
                    break;

                case SealedDeck:
                    ReplaceStarterDeck(
                        "Sealed Strike",
                        "Sealed Strike",
                        "Sealed Defend",
                        "Sealed Defend",
                        "Sealed Rare",
                        "Sealed Skill",
                        "Sealed Attack",
                        "Sealed Power",
                        "Sealed Utility",
                        "Sealed Finisher");
                    NextChoiceId += 10;
                    break;

                case Insanity:
                    ReplaceStarterDeck(Enumerable.Range(1, 50).Select(index => $"Insanity Card {index}").ToArray());
                    NextChoiceId += 1;
                    break;

                case Specialized:
                    _deck.AddRange(Enumerable.Repeat("Specialized Card", 5));
                    NextChoiceId += 1;
                    break;

                case AllStar:
                    _deck.AddRange(["Colorless One", "Colorless Two", "Colorless Three", "Colorless Four", "Colorless Five"]);
                    NextChoiceId += 1;
                    break;

                default:
                    _deck.Add($"Unknown Modifier Reward {modifierId}");
                    NextChoiceId += 1;
                    break;
            }
        }

        public void ApplyNeowBlessingByIdentity(string optionIdentity)
        {
            switch (optionIdentity)
            {
                case "NEOW.pages.INITIAL.options.SILKEN_TRESS":
                    _relics.Add("Silken Tress");
                    break;

                case "NEOW.pages.INITIAL.options.GOLD":
                    _gold += 99;
                    break;

                default:
                    _relics.Add(optionIdentity);
                    break;
            }

            NextChoiceId += 1;
            NeowEventRngCounter += 1;
        }

        public string StateSignature()
        {
            return string.Join(
                " | ",
                $"Player={PlayerId}",
                $"Slot={SlotIndex}",
                $"Gold={_gold}",
                $"Relics={string.Join(",", _relics.OrderBy(relic => relic, StringComparer.Ordinal))}",
                $"Deck={string.Join(",", _deck.OrderBy(card => card, StringComparer.Ordinal))}",
                $"NextChoiceId={NextChoiceId}",
                $"NeowRngCounter={NeowEventRngCounter}");
        }

        private void ReplaceStarterDeck(params string[] cards)
        {
            _deck.Clear();
            _deck.AddRange(cards);
        }
    }

    private sealed class DeterministicRng
    {
        private uint _state;

        public DeterministicRng(uint seed, int counter)
        {
            _state = seed == 0 ? 0x6D2B79F5u : seed;
            for (int i = 0; i < counter; i++)
            {
                NextUInt();
            }
        }

        public int NextInt(int maxExclusive)
        {
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        private uint NextUInt()
        {
            unchecked
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }
        }
    }

    private readonly record struct StartupStepMessage(
        ulong SenderNetId,
        int SyncEpoch,
        int StepIndex,
        int TotalStepCount,
        string ModifierId,
        uint NextChoiceId);

    private readonly record struct ModifierBootstrapSpec(
        string ModifierId,
        int RunModifierIndex);
}
