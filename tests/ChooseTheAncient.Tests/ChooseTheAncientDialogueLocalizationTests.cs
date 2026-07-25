using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode;
using Xunit;

namespace ChooseTheAncient.Tests;

public sealed class ChooseTheAncientDialogueLocalizationTests
{
    private const string Root =
        "choose_the_ancient.second_round.dialogue.";

    private static readonly DialogueLocalizationLookupContext
        ReactionContext = new(
            SpeakerRole: DialogueSpeakerRole.Reaction,
            SpeakerAncientEntry: "DARV",
            OtherAncientEntry: "NEOW",
            CharacterEntry: "IRONCLAD",
            ActEntry: "HIVE");

    [Fact]
    public void Reaction_dialogue_uses_expected_priority()
    {
        string[] prefixes = ChooseTheAncientDialogueLocalizationRules
            .BuildPrefixSearchOrder(Root, ReactionContext)
            .ToArray();

        Assert.Equal(
        [
            $"{Root}reaction.DARV.other_ancient.NEOW.character.IRONCLAD.act.HIVE.",
            $"{Root}reaction.DARV.other_ancient.NEOW.character.IRONCLAD.",
            $"{Root}reaction.DARV.other_ancient.NEOW.act.HIVE.",
            $"{Root}reaction.DARV.other_ancient.NEOW.",
            $"{Root}reaction.DARV.character.IRONCLAD.act.HIVE.",
            $"{Root}reaction.DARV.character.IRONCLAD.",
            $"{Root}reaction.DARV.act.HIVE.",
            $"{Root}reaction.DARV."
        ],
        prefixes);
    }

    [Fact]
    public void Branch_dialogue_uses_the_same_qualifier_priority()
    {
        string[] prefixes = ChooseTheAncientDialogueLocalizationRules
            .BuildBranchPrefixSearchOrder(
                Root,
                ReactionContext,
                "affection",
                "friendly")
            .ToArray();

        Assert.Equal(
        [
            $"{Root}reaction.DARV.affection.friendly.other_ancient.NEOW.character.IRONCLAD.act.HIVE.",
            $"{Root}reaction.DARV.affection.friendly.other_ancient.NEOW.character.IRONCLAD.",
            $"{Root}reaction.DARV.affection.friendly.other_ancient.NEOW.act.HIVE.",
            $"{Root}reaction.DARV.affection.friendly.other_ancient.NEOW.",
            $"{Root}reaction.DARV.affection.friendly.character.IRONCLAD.act.HIVE.",
            $"{Root}reaction.DARV.affection.friendly.character.IRONCLAD.",
            $"{Root}reaction.DARV.affection.friendly.act.HIVE.",
            $"{Root}reaction.DARV.affection.friendly."
        ],
        prefixes);
    }

    [Fact]
    public void Suppressed_dialogue_uses_suppressed_role()
    {
        DialogueLocalizationLookupContext context = new(
            SpeakerRole: DialogueSpeakerRole.Suppressed,
            SpeakerAncientEntry: "NEOW",
            OtherAncientEntry: "DARV",
            CharacterEntry: null,
            ActEntry: null);

        string[] prefixes = ChooseTheAncientDialogueLocalizationRules
            .BuildPrefixSearchOrder(Root, context)
            .ToArray();

        Assert.Equal(
        [
            $"{Root}suppressed.NEOW.other_ancient.DARV.",
            $"{Root}suppressed.NEOW."
        ],
        prefixes);

        Assert.Equal(
            $"{Root}suppressed.default.",
            ChooseTheAncientDialogueLocalizationRules.BuildDefaultPrefix(
                Root,
                DialogueSpeakerRole.Suppressed));
    }

    [Fact]
    public void Namespaced_entries_do_not_collide()
    {
        DialogueLocalizationLookupContext context = new(
            SpeakerRole: DialogueSpeakerRole.Reaction,
            SpeakerAncientEntry: "SPEAKER",
            OtherAncientEntry: "SHARED",
            CharacterEntry: "SHARED",
            ActEntry: "SHARED");

        string[] prefixes = ChooseTheAncientDialogueLocalizationRules
            .BuildPrefixSearchOrder(Root, context)
            .ToArray();

        Assert.Equal(prefixes.Length, prefixes.Distinct().Count());
        Assert.Contains(
            $"{Root}reaction.SPEAKER.other_ancient.SHARED.",
            prefixes);
        Assert.Contains(
            $"{Root}reaction.SPEAKER.character.SHARED.",
            prefixes);
        Assert.Contains(
            $"{Root}reaction.SPEAKER.act.SHARED.",
            prefixes);
    }

    [Theory]
    [InlineData("dialogue.0", 0)]
    [InlineData("dialogue.10", 10)]
    public void Direct_nonnegative_numeric_children_are_variations(
        string key,
        int expectedIndex)
    {
        bool result =
            ChooseTheAncientDialogueLocalizationRules.TryGetDirectNumericIndex(
                key,
                "dialogue.",
                out int index);

        Assert.True(result);
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData("dialogue.-1")]
    [InlineData("dialogue.character.IRONCLAD.0")]
    public void Negative_or_nested_children_are_not_variations(string key)
    {
        bool result =
            ChooseTheAncientDialogueLocalizationRules.TryGetDirectNumericIndex(
                key,
                "dialogue.",
                out _);

        Assert.False(result);
    }
}
