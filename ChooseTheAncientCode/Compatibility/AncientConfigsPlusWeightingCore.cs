using System;
using System.Collections.Generic;
using System.Linq;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

internal sealed record AncientConfigsPlusCandidate<TAncient>(
    TAncient Model,
    string Id,
    string WeightKey,
    bool ForceSpawn);

internal static class AncientConfigsPlusWeightingCore
{
    internal static List<TAncient> FilterCandidatesWithPositiveWeights<TAncient>(
        IEnumerable<AncientConfigsPlusCandidate<TAncient>> collectedCandidates,
        IReadOnlyDictionary<string, int> weights)
    /*
     * Mirrors AncientConfigsPlus' enable/disable behavior from the point where CTA has already collected all valid candidates.
     * Missing AncientConfigsPlus keys are treated as weight 0, matching ACP's own GetWeightedAncient lookup.
     */
    {
        List<WeightedCandidate<TAncient>> weightedCandidates =
            GetDistinctPositiveWeightedCandidates(collectedCandidates, weights);

        List<TAncient> result = new(weightedCandidates.Count);
        foreach (WeightedCandidate<TAncient> weightedCandidate in weightedCandidates)
            result.Add(weightedCandidate.Candidate.Model);

        return result;
    }

    internal static List<TAncient> SelectWeightedBallotWithoutReplacement<TAncient>(
        IEnumerable<AncientConfigsPlusCandidate<TAncient>> collectedCandidates,
        IReadOnlyDictionary<string, int> weights,
        int requestedCount,
        Func<int, int> nextInt)
    /*
     * Selects a CTA ballot from the already-collected candidate pool using AncientConfigsPlus weights.
     * Forced-spawn candidates keep priority, then remaining slots are drawn by weighted sampling without replacement.
     */
    {
        if (requestedCount <= 0)
            return new List<TAncient>();

        List<WeightedCandidate<TAncient>> remaining =
            GetDistinctPositiveWeightedCandidates(collectedCandidates, weights);

        if (remaining.Count <= requestedCount)
        {
            List<TAncient> allEnabled = new(remaining.Count);
            foreach (WeightedCandidate<TAncient> weightedCandidate in remaining)
                allEnabled.Add(weightedCandidate.Candidate.Model);

            return allEnabled;
        }

        List<TAncient> selected = new();
        HashSet<string> selectedIds = new(StringComparer.Ordinal);

        foreach (WeightedCandidate<TAncient> forced in remaining.Where(candidate => candidate.Candidate.ForceSpawn))
        {
            if (selected.Count >= requestedCount)
                return selected;

            if (selectedIds.Add(forced.Candidate.Id))
                selected.Add(forced.Candidate.Model);
        }

        remaining = remaining
            .Where(candidate => !selectedIds.Contains(candidate.Candidate.Id))
            .ToList();

        while (selected.Count < requestedCount && remaining.Count > 0)
        {
            int totalWeight = remaining.Sum(candidate => candidate.Weight);
            if (totalWeight <= 0)
                break;

            int roll = nextInt(totalWeight);
            if (roll < 0 || roll >= totalWeight)
                throw new ArgumentOutOfRangeException(nameof(nextInt), $"Weighted roll {roll} was outside [0, {totalWeight}).");

            int cumulative = 0;
            for (int i = 0; i < remaining.Count; i++)
            {
                cumulative += remaining[i].Weight;
                if (roll >= cumulative)
                    continue;

                WeightedCandidate<TAncient> chosen = remaining[i];
                remaining.RemoveAt(i);

                if (selectedIds.Add(chosen.Candidate.Id))
                    selected.Add(chosen.Candidate.Model);

                break;
            }
        }

        return selected;
    }

    private static List<WeightedCandidate<TAncient>> GetDistinctPositiveWeightedCandidates<TAncient>(
        IEnumerable<AncientConfigsPlusCandidate<TAncient>> collectedCandidates,
        IReadOnlyDictionary<string, int> weights)
    {
        Dictionary<string, AncientConfigsPlusCandidate<TAncient>> distinctById = new(StringComparer.Ordinal);

        foreach (AncientConfigsPlusCandidate<TAncient> candidate in collectedCandidates)
        {
            if (!distinctById.ContainsKey(candidate.Id))
                distinctById.Add(candidate.Id, candidate);
        }

        List<WeightedCandidate<TAncient>> weightedCandidates = new();

        foreach (AncientConfigsPlusCandidate<TAncient> candidate in distinctById.Values)
        {
            int weight = weights.TryGetValue(candidate.WeightKey, out int configuredWeight)
                ? configuredWeight
                : 0;

            if (weight > 0)
                weightedCandidates.Add(new WeightedCandidate<TAncient>(candidate, weight));
        }

        weightedCandidates.Sort((left, right) =>
            string.Compare(left.Candidate.Id, right.Candidate.Id, StringComparison.Ordinal));

        return weightedCandidates;
    }

    private sealed record WeightedCandidate<TAncient>(
        AncientConfigsPlusCandidate<TAncient> Candidate,
        int Weight);
}
