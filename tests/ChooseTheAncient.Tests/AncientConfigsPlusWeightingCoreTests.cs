using ChooseTheAncient.ChooseTheAncientCode.Compatibility;
using Xunit;

namespace ChooseTheAncient.Tests;

public sealed class AncientConfigsPlusWeightingCoreTests
{
    [Fact]
    public void Act1_VanillaAncients_WithChangedConfigSettings_DisablesWeightZeroAncients()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("BONFIRE_SPIRITS", "BonfireSpirits"),
            Vanilla("AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["BonfireSpirits"] = 0,
            ["Aurora"] = 7
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["AURORA", "NEOW"], filtered.Select(ancient => ancient.Id));
        Assert.All(filtered, ancient => Assert.False(ancient.IsCustom));
    }

    [Fact]
    public void Act1_VanillaAncients_WithChangedConfigSettings_MissingWeightsAreDisabled()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("BONFIRE_SPIRITS", "BonfireSpirits"),
            Vanilla("AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 3,
            ["Aurora"] = 2
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["AURORA", "NEOW"], filtered.Select(ancient => ancient.Id));
        Assert.DoesNotContain(filtered, ancient => ancient.Id == "BONFIRE_SPIRITS");
    }

    [Fact]
    public void Act1_VanillaAncients_WithChangedConfigSettings_RequestingMoreThanEnabledReturnsAllEnabled()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("BONFIRE_SPIRITS", "BonfireSpirits"),
            Vanilla("AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 3,
            ["BonfireSpirits"] = 0,
            ["Aurora"] = 2
        };

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 5,
                nextInt: _ => throw new InvalidOperationException("RNG should not be used when every enabled candidate fits."));

        Assert.Equal(["AURORA", "NEOW"], ballot.Select(ancient => ancient.Id));
    }

    [Fact]
    public void Act1_VanillaAncients_WithSomeAct2Ancients_UsesCollectedPoolRatherThanRemovingSourceAct2()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("DARV", "Darv"),
            Vanilla("VAKUU", "Vakuu")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["Darv"] = 2,
            ["Vakuu"] = 0
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["DARV", "NEOW"], filtered.Select(ancient => ancient.Id));
        Assert.All(filtered, ancient => Assert.False(ancient.IsCustom));
    }

    [Fact]
    public void Act1_VanillaAncients_WithSomeAct2Ancients_CanSelectAct2AncientWhenItWasCollectedAndWeighted()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("DARV", "Darv"),
            Vanilla("VAKUU", "Vakuu")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["Darv"] = 9,
            ["Vakuu"] = 0
        };

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 1,
                nextInt: _ => 1);

        Assert.Equal(["DARV"], ballot.Select(ancient => ancient.Id));
    }

    [Fact]
    public void Act1_VanillaAncients_WithSomeAct2Ancients_WeightZeroStillDisablesCollectedAct2Ancient()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Vanilla("DARV", "Darv"),
            Vanilla("VAKUU", "Vakuu")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["Darv"] = 0,
            ["Vakuu"] = 6
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["NEOW", "VAKUU"], filtered.Select(ancient => ancient.Id));
        Assert.DoesNotContain(filtered, ancient => ancient.Id == "DARV");
    }

    [Fact]
    public void Act1_MixOfCustomAndVanillaAncients_AppliesSameWeightsToBoth()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["PhoenixAncient"] = 100,
            ["EchoAncient"] = 0
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["CUSTOM_PHOENIX", "NEOW"], filtered.Select(ancient => ancient.Id));
        Assert.Contains(filtered, ancient => ancient is { Id: "CUSTOM_PHOENIX", IsCustom: true });
        Assert.Contains(filtered, ancient => ancient is { Id: "NEOW", IsCustom: false });

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 1,
                nextInt: _ => 0);

        Assert.Equal(["CUSTOM_PHOENIX"], ballot.Select(ancient => ancient.Id));
        Assert.All(ballot, ancient => Assert.True(ancient.IsCustom));
    }

    [Fact]
    public void Act1_MixOfCustomAndVanillaAncients_CustomMissingWeightIsDisabled()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["PhoenixAncient"] = 4
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["CUSTOM_PHOENIX", "NEOW"], filtered.Select(ancient => ancient.Id));
        Assert.DoesNotContain(filtered, ancient => ancient.Id == "CUSTOM_ECHO");
    }

    [Fact]
    public void Act1_MixOfCustomAndVanillaAncients_VanillaCanWinWeightedRollAgainstCustom()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 3,
            ["PhoenixAncient"] = 2,
            ["EchoAncient"] = 0
        };

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 1,
                nextInt: _ => 2);

        Assert.Equal(["NEOW"], ballot.Select(ancient => ancient.Id));
        Assert.All(ballot, ancient => Assert.False(ancient.IsCustom));
    }

    [Fact]
    public void Act1_JustCustomAncients_FiltersAndSelectsOnlyEnabledCustomAncients()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["PhoenixAncient"] = 0,
            ["EchoAncient"] = 5
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["CUSTOM_ECHO"], filtered.Select(ancient => ancient.Id));
        Assert.All(filtered, ancient => Assert.True(ancient.IsCustom));

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 3,
                nextInt: _ => throw new InvalidOperationException("RNG should not be used when only one candidate is enabled."));

        Assert.Equal(["CUSTOM_ECHO"], ballot.Select(ancient => ancient.Id));
        Assert.All(ballot, ancient => Assert.True(ancient.IsCustom));
    }

    [Fact]
    public void Act1_JustCustomAncients_DeduplicatesByAncientIdBeforeWeighting()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["PhoenixAncient"] = 10,
            ["EchoAncient"] = 10
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Equal(["CUSTOM_ECHO", "CUSTOM_PHOENIX"], filtered.Select(ancient => ancient.Id));
        Assert.Equal(filtered.Select(ancient => ancient.Id).Distinct().Count(), filtered.Count);
    }

    [Fact]
    public void Act1_JustCustomAncients_ReturnsEmptyWhenEveryCustomAncientIsDisabled()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Custom("CUSTOM_PHOENIX", "PhoenixAncient"),
            Custom("CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["PhoenixAncient"] = 0,
            ["EchoAncient"] = 0
        };

        List<MockAncient> filtered =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(collectedAncients, act1Weights);

        Assert.Empty(filtered);
    }

    [Fact]
    public void Act1_ForcedSpawnCandidate_IsSelectedBeforeWeightedRolls()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("NEOW", "Neow"),
            Custom("CUSTOM_FORCED", "ForcedAncient", forceSpawn: true),
            Custom("CUSTOM_HIGH_WEIGHT", "HighWeightAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["ForcedAncient"] = 1,
            ["HighWeightAncient"] = 999
        };

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                act1Weights,
                requestedCount: 1,
                nextInt: _ => throw new InvalidOperationException("RNG should not be used before the forced ancient fills the ballot."));

        Assert.Equal(["CUSTOM_FORCED"], ballot.Select(ancient => ancient.Id));
        Assert.All(ballot, ancient => Assert.True(ancient.IsCustom));
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(3, "B")]
    [InlineData(4, "C")]
    [InlineData(9, "C")]
    public void WeightedRollBoundaries_SelectExpectedCandidate(int roll, string expectedId)
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("A", "AncientA"),
            Vanilla("B", "AncientB"),
            Vanilla("C", "AncientC")
        ];

        Dictionary<string, int> weights = new()
        {
            ["AncientA"] = 1,
            ["AncientB"] = 3,
            ["AncientC"] = 6
        };

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                weights,
                requestedCount: 1,
                nextInt: maxExclusive =>
                {
                    Assert.Equal(10, maxExclusive);
                    return roll;
                });

        Assert.Equal([expectedId], ballot.Select(ancient => ancient.Id));
    }

    [Fact]
    public void WeightedSelectionWithoutReplacement_UsesUpdatedWeightsAfterEachPick()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("A", "AncientA"),
            Vanilla("B", "AncientB"),
            Vanilla("C", "AncientC")
        ];

        Dictionary<string, int> weights = new()
        {
            ["AncientA"] = 1,
            ["AncientB"] = 3,
            ["AncientC"] = 6
        };

        Queue<int> rolls = new(new[] { 9, 0 });

        List<MockAncient> ballot =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    if (maxExclusive == 10)
                        return rolls.Dequeue();

                    Assert.Equal(4, maxExclusive);
                    return rolls.Dequeue();
                });

        Assert.Equal(["C", "A"], ballot.Select(ancient => ancient.Id));
        Assert.Empty(rolls);
    }

    [Fact]
    public void WeightedSelection_RejectsOutOfRangeRngRoll()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("A", "AncientA"),
            Vanilla("B", "AncientB")
        ];

        Dictionary<string, int> weights = new()
        {
            ["AncientA"] = 1,
            ["AncientB"] = 1
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                collectedAncients,
                weights,
                requestedCount: 1,
                nextInt: maxExclusive => maxExclusive));
    }

    [Fact]
    public void WeightedSelectionDistribution_WithFixedSeed_TracksConfiguredWeights()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> collectedAncients =
        [
            Vanilla("A", "AncientA"),
            Vanilla("B", "AncientB"),
            Vanilla("C", "AncientC")
        ];

        Dictionary<string, int> weights = new()
        {
            ["AncientA"] = 1,
            ["AncientB"] = 3,
            ["AncientC"] = 6
        };

        Random random = new(12345);
        Dictionary<string, int> counts = new(StringComparer.Ordinal)
        {
            ["A"] = 0,
            ["B"] = 0,
            ["C"] = 0
        };

        const int trials = 20_000;

        for (int i = 0; i < trials; i++)
        {
            List<MockAncient> ballot =
                AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                    collectedAncients,
                    weights,
                    requestedCount: 1,
                    nextInt: random.Next);

            counts[ballot.Single().Id]++;
        }

        double aShare = counts["A"] / (double)trials;
        double bShare = counts["B"] / (double)trials;
        double cShare = counts["C"] / (double)trials;

        Assert.InRange(aShare, 0.08, 0.12);
        Assert.InRange(bShare, 0.27, 0.33);
        Assert.InRange(cShare, 0.57, 0.63);
    }


    [Fact]
    public void Act1_SharedVanillaAncients_WeightedSampling_ReturnsExpectedEndListAfterChangedConfigWeights()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Vanilla("B_BONFIRE_SPIRITS", "BonfireSpirits"),
            Vanilla("C_AURORA", "Aurora"),
            Vanilla("D_SERPENT", "Serpent")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["BonfireSpirits"] = 2,
            ["Aurora"] = 3,
            ["Serpent"] = 4
        };

        Queue<int> rolls = new(new[] { 0, 4 });

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    int roll = rolls.Dequeue();

                    if (roll == 0)
                        Assert.Equal(10, maxExclusive);
                    else
                        Assert.Equal(9, maxExclusive);

                    return roll;
                });

        Assert.Equal(["A_NEOW", "C_AURORA"], endList.Select(ancient => ancient.Id));
        Assert.All(endList, ancient => Assert.False(ancient.IsCustom));
        Assert.Empty(rolls);
    }

    [Fact]
    public void Act1_SharedVanillaAncients_WithAct2Ancients_WeightedSamplingUsesTheCollectedSharedList()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Vanilla("B_ACT2_DARV", "Darv"),
            Vanilla("C_ACT2_VAKUU", "Vakuu"),
            Vanilla("D_AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["Darv"] = 5,
            ["Vakuu"] = 0,
            ["Aurora"] = 2
        };

        Queue<int> rolls = new(new[] { 1, 2 });

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    int roll = rolls.Dequeue();

                    if (roll == 1)
                        Assert.Equal(8, maxExclusive);
                    else
                        Assert.Equal(3, maxExclusive);

                    return roll;
                });

        Assert.Equal(["B_ACT2_DARV", "D_AURORA"], endList.Select(ancient => ancient.Id));
        Assert.DoesNotContain(endList, ancient => ancient.Id == "C_ACT2_VAKUU");
        Assert.Empty(rolls);
    }

    [Fact]
    public void Act1_SharedMixOfCustomAndVanillaAncients_WeightedSamplingReturnsExpectedEndList()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Custom("A_CUSTOM_ECHO", "EchoAncient"),
            Vanilla("B_NEOW", "Neow"),
            Custom("C_CUSTOM_PHOENIX", "PhoenixAncient"),
            Vanilla("D_AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["EchoAncient"] = 1,
            ["Neow"] = 4,
            ["PhoenixAncient"] = 5,
            ["Aurora"] = 0
        };

        Queue<int> rolls = new(new[] { 0, 2 });

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    int roll = rolls.Dequeue();

                    if (roll == 0)
                        Assert.Equal(10, maxExclusive);
                    else
                        Assert.Equal(9, maxExclusive);

                    return roll;
                });

        Assert.Equal(["A_CUSTOM_ECHO", "B_NEOW"], endList.Select(ancient => ancient.Id));
        Assert.Contains(endList, ancient => ancient is { Id: "A_CUSTOM_ECHO", IsCustom: true });
        Assert.Contains(endList, ancient => ancient is { Id: "B_NEOW", IsCustom: false });
        Assert.DoesNotContain(endList, ancient => ancient.Id == "D_AURORA");
        Assert.Empty(rolls);
    }

    [Fact]
    public void Act1_SharedJustCustomAncients_WeightedSamplingReturnsExpectedEndList()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Custom("A_CUSTOM_ECHO", "EchoAncient"),
            Custom("B_CUSTOM_MIRROR", "MirrorAncient"),
            Custom("C_CUSTOM_PHOENIX", "PhoenixAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["EchoAncient"] = 1,
            ["MirrorAncient"] = 3,
            ["PhoenixAncient"] = 6
        };

        Queue<int> rolls = new(new[] { 9, 1 });

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    int roll = rolls.Dequeue();

                    if (roll == 9)
                        Assert.Equal(10, maxExclusive);
                    else
                        Assert.Equal(4, maxExclusive);

                    return roll;
                });

        Assert.Equal(["C_CUSTOM_PHOENIX", "B_CUSTOM_MIRROR"], endList.Select(ancient => ancient.Id));
        Assert.All(endList, ancient => Assert.True(ancient.IsCustom));
        Assert.Empty(rolls);
    }

    [Fact]
    public void Act1_SharedAncients_ForcedCustomAncientIsInEndListBeforeWeightedSamplingFillsRemainingSlots()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Custom("B_CUSTOM_FORCED", "ForcedAncient", forceSpawn: true),
            Custom("C_CUSTOM_HIGH_WEIGHT", "HighWeightAncient"),
            Vanilla("D_AURORA", "Aurora")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["ForcedAncient"] = 1,
            ["HighWeightAncient"] = 100,
            ["Aurora"] = 2
        };

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 2,
                nextInt: maxExclusive =>
                {
                    Assert.Equal(103, maxExclusive);
                    return 0;
                });

        Assert.Equal(["B_CUSTOM_FORCED", "A_NEOW"], endList.Select(ancient => ancient.Id));
        Assert.True(endList[0].IsCustom);
    }

    [Fact]
    public void Act1_SharedAncients_WhenRequestedCountEqualsEnabledCountReturnsSortedEndListWithoutRng()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Custom("C_CUSTOM_PHOENIX", "PhoenixAncient"),
            Vanilla("A_NEOW", "Neow"),
            Vanilla("B_ACT2_DARV", "Darv"),
            Custom("D_CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 5,
            ["Darv"] = 4,
            ["PhoenixAncient"] = 3,
            ["EchoAncient"] = 0
        };

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 3,
                nextInt: _ => throw new InvalidOperationException("RNG should not be used when every enabled candidate fits."));

        Assert.Equal(["A_NEOW", "B_ACT2_DARV", "C_CUSTOM_PHOENIX"], endList.Select(ancient => ancient.Id));
        Assert.DoesNotContain(endList, ancient => ancient.Id == "D_CUSTOM_ECHO");
    }


    private static AncientConfigsPlusCandidate<MockAncient> Vanilla(
        string id,
        string weightKey,
        bool forceSpawn = false) =>
        new(new MockAncient(id, isCustom: false), id, weightKey, forceSpawn);

    private static AncientConfigsPlusCandidate<MockAncient> Custom(
        string id,
        string weightKey,
        bool forceSpawn = false) =>
        new(new MockAncient(id, isCustom: true), id, weightKey, forceSpawn);

    private sealed class MockAncient
    {
        public MockAncient(string id, bool isCustom)
        {
            Id = id;
            IsCustom = isCustom;
        }

        public string Id { get; }
        public bool IsCustom { get; }
    }
}
