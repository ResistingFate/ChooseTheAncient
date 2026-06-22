using ChooseTheAncient.ChooseTheAncientCode.Compatibility;
using Xunit;

namespace ChooseTheAncient.Tests;

public sealed class SharedAncientWeightSamplingTests
{
    [Fact]
    public void SharedImplementation_VanillaAncientsWithChangedWeights_ReturnsExpectedEndList()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Vanilla("B_BONFIRE_SPIRITS", "BonfireSpirits"),
            Vanilla("C_AURORA", "Aurora"),
            Vanilla("D_DISABLED", "DisabledAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["BonfireSpirits"] = 3,
            ["Aurora"] = 6,
            ["DisabledAncient"] = 0
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

        Assert.Equal(["C_AURORA", "B_BONFIRE_SPIRITS"], endList.Select(ancient => ancient.Id));
        Assert.DoesNotContain(endList, ancient => ancient.Id == "D_DISABLED");
        Assert.Empty(rolls);
    }

    [Fact]
    public void SharedImplementation_VanillaPlusAct2AncientsInAct1_UsesCollectedSharedList()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Vanilla("B_ACT2_DARV", "Darv"),
            Vanilla("C_ACT2_GREMLIN", "GremlinAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["Darv"] = 100,
            ["GremlinAncient"] = 0
        };

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 1,
                nextInt: maxExclusive =>
                {
                    Assert.Equal(101, maxExclusive);
                    return 1;
                });

        Assert.Equal(["B_ACT2_DARV"], endList.Select(ancient => ancient.Id));
        Assert.DoesNotContain(endList, ancient => ancient.Id == "C_ACT2_GREMLIN");
    }

    [Fact]
    public void SharedImplementation_CustomAndVanillaMix_AppliesWeightsToBothKinds()
    {
        List<AncientConfigsPlusCandidate<MockAncient>> sharedAncients =
        [
            Vanilla("A_NEOW", "Neow"),
            Custom("B_CUSTOM_PHOENIX", "PhoenixAncient"),
            Vanilla("C_AURORA", "Aurora"),
            Custom("D_CUSTOM_ECHO", "EchoAncient")
        ];

        Dictionary<string, int> act1Weights = new()
        {
            ["Neow"] = 1,
            ["PhoenixAncient"] = 2,
            ["Aurora"] = 3,
            ["EchoAncient"] = 4
        };

        Queue<int> rolls = new(new[] { 9, 2, 0 });

        List<MockAncient> endList =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                sharedAncients,
                act1Weights,
                requestedCount: 3,
                nextInt: maxExclusive =>
                {
                    int roll = rolls.Dequeue();

                    if (roll == 9)
                        Assert.Equal(10, maxExclusive);
                    else if (roll == 2)
                        Assert.Equal(6, maxExclusive);
                    else
                        Assert.Equal(4, maxExclusive);

                    return roll;
                });

        Assert.Equal(["D_CUSTOM_ECHO", "B_CUSTOM_PHOENIX", "A_NEOW"], endList.Select(ancient => ancient.Id));
        Assert.Contains(endList, ancient => ancient.IsCustom);
        Assert.Contains(endList, ancient => !ancient.IsCustom);
        Assert.Empty(rolls);
    }

    [Fact]
    public void SharedImplementation_OnlyCustomAncients_ReturnsWeightedCustomEndList()
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
    public void SharedImplementation_ForcedCustomAncient_IsPlacedBeforeWeightedFill()
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
    public void SharedImplementation_WhenRequestedCountEqualsEnabledCount_ReturnsSortedEndListWithoutRng()
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
