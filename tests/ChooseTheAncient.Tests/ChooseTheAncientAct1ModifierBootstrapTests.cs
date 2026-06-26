using System;
using System.Collections.Generic;
using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode;
using Xunit;

namespace ChooseTheAncient.Tests;

public sealed class ChooseTheAncientAct1ModifierBootstrapTests
{
    private const string Draft = "DRAFT";
    private const string SealedDeck = "SEALED_DECK";
    private const string Insanity = "INSANITY";
    private const string Specialized = "SPECIALIZED";
    private const string AllStar = "ALL_STAR";

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
    public void Valid_act1_card_modifier_combinations_are_supported_without_reordering_unrelated_modifiers(
        string[] modifierIds)
    {
        IReadOnlyList<ModifierBootstrapSpec> ordered = OrderLikeCoordinator(
            modifierIds.Select((id, index) => new ModifierBootstrapSpec(id, index)));

        Assert.Equal(modifierIds, ordered.Select(action => action.ModifierId));
        Assert.Equal(Enumerable.Range(0, modifierIds.Length), ordered.Select(action => action.RunModifierIndex));
    }

    [Theory]
    [MemberData(nameof(ValidAct1CardModifierCombinations))]
    public void Valid_act1_card_modifier_combinations_still_allow_neow_blessing_mode_when_neow_is_chosen(
        string[] modifierIds)
    {
        Assert.NotEmpty(modifierIds);

        ChooseTheAncientFlowState flow = new();
        EnableNeowBlessingModeIfNeowWasChosen(flow, "NEOW");

        Assert.True(flow.ForceNeowBlessingMode);
        Assert.True(flow.ForceAct1NeowBlessingMode);
    }

    [Theory]
    [InlineData("GOLDEN_IDOL")]
    [InlineData("BONFIRE_SPIRITS")]
    [InlineData("")]
    [InlineData(null)]
    public void Neow_blessing_mode_is_not_enabled_for_non_neow_act1_ancients(string? ancientId)
    {
        ChooseTheAncientFlowState flow = new();

        EnableNeowBlessingModeIfNeowWasChosen(flow, ancientId);

        Assert.False(flow.ForceNeowBlessingMode);
    }

    [Fact]
    public void Unknown_modifier_bootstrap_actions_are_kept_in_run_modifier_order()
    {
        ModifierBootstrapSpec[] input =
        [
            new("THIRD_PARTY_STARTER_PACK", 0),
            new(AllStar, 1),
            new(Specialized, 2)
        ];

        IReadOnlyList<ModifierBootstrapSpec> ordered = OrderLikeCoordinator(input);

        Assert.Equal(
            ["THIRD_PARTY_STARTER_PACK", AllStar, Specialized],
            ordered.Select(action => action.ModifierId));
    }

    [Fact]
    public void Only_the_sealed_deck_before_draft_dependency_changes_modifier_bootstrap_order()
    {
        ModifierBootstrapSpec[] input =
        [
            new(Draft, 0),
            new("THIRD_PARTY_STARTER_PACK", 1),
            new(SealedDeck, 2),
            new(AllStar, 3)
        ];

        IReadOnlyList<ModifierBootstrapSpec> ordered = OrderLikeCoordinator(input);

        Assert.Equal(
            [SealedDeck, Draft, "THIRD_PARTY_STARTER_PACK", AllStar],
            ordered.Select(action => action.ModifierId));
    }

    [Fact]
    public void Startup_bootstrap_flow_state_records_step_completion_without_losing_latest_choice_id()
    {
        ChooseTheAncientFlowState flow = new();
        int epoch = flow.BeginAct1StartupBootstrapSyncEpoch();

        Assert.Equal(
            StartupStepRecordResult.Added,
            flow.RecordPendingStartupStepCompletionMessage(
                epoch,
                stepIndex: 0,
                playerNetId: 1000,
                totalStepCount: 3,
                modifierId: Specialized,
                nextChoiceId: 4));

        Assert.Equal(
            StartupStepRecordResult.Duplicate,
            flow.RecordPendingStartupStepCompletionMessage(
                epoch,
                stepIndex: 0,
                playerNetId: 1000,
                totalStepCount: 3,
                modifierId: Specialized,
                nextChoiceId: 4));

        Assert.Equal(
            StartupStepRecordResult.Updated,
            flow.RecordPendingStartupStepCompletionMessage(
                epoch,
                stepIndex: 0,
                playerNetId: 1000,
                totalStepCount: 3,
                modifierId: Specialized,
                nextChoiceId: 9));

        StartupStepCompletionInfo info = flow
            .GetPendingStartupStepCompletionMessagesForEpoch(epoch, stepIndex: 0)[1000];

        Assert.Equal(9u, info.NextChoiceId);
        Assert.True(flow.HasPendingStartupStepCompletionMessageForEpoch(epoch, 0, 1000));
        Assert.Equal(1, flow.GetPendingStartupStepCompletionMessageCountForEpoch(epoch, 0));
    }

    [Fact]
    public void Starting_a_new_bootstrap_epoch_clears_previous_pending_step_messages()
    {
        ChooseTheAncientFlowState flow = new();
        int firstEpoch = flow.BeginAct1StartupBootstrapSyncEpoch();

        flow.RecordPendingStartupStepCompletionMessage(
            firstEpoch,
            stepIndex: 0,
            playerNetId: 1,
            totalStepCount: 1,
            modifierId: AllStar,
            nextChoiceId: 2);

        int secondEpoch = flow.BeginAct1StartupBootstrapSyncEpoch();

        Assert.NotEqual(firstEpoch, secondEpoch);
        Assert.False(flow.HasPendingStartupStepCompletionMessageForEpoch(firstEpoch, 0, 1));
        Assert.Equal("<none>", flow.DescribePendingStartupStepCompletionMessages());
    }

    private static List<ModifierBootstrapSpec> OrderLikeCoordinator(IEnumerable<ModifierBootstrapSpec> actions)
    {
        List<ModifierBootstrapSpec> ordered = actions
            .OrderBy(action => action.RunModifierIndex)
            .ToList();

        MoveModifierBeforeIfBothPresent(ordered, SealedDeck, Draft);
        return ordered;
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

    private static void EnableNeowBlessingModeIfNeowWasChosen(
        ChooseTheAncientFlowState flow,
        string? chosenAncientId)
    {
        if (!string.Equals(chosenAncientId, "NEOW", StringComparison.OrdinalIgnoreCase))
            return;

        flow.ForceNeowBlessingMode = true;
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

    private readonly record struct ModifierBootstrapSpec(
        string ModifierId,
        int RunModifierIndex);
}
