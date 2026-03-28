using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class AncientBanHelpers
{
    public static RunState? GetRunState(RunManager runManager)
    {
        return Traverse.Create(runManager)
            .Property("State")
            .GetValue<RunState>();
    }

    public static List<AncientEventModel> BuildCandidatePool(ActModel act, RunState runState)
    {
        // Important: use the run's merged unlock state, not any individual player's unlock state.
        List<AncientEventModel> sharedSubset = Traverse.Create(act)
            .Field("_sharedAncientSubset")
            .GetValue<List<AncientEventModel>>() ?? new List<AncientEventModel>();

        return act
            .GetUnlockedAncients(runState.UnlockState)
            .Concat(sharedSubset)
            .Where(a => a is not null)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.Entry)
            .ToList();
    }

    public static void SetChosenAncient(ActModel act, AncientEventModel chosenAncient)
    {
        RoomSet? rooms = Traverse.Create(act)
            .Field("_rooms")
            .GetValue<RoomSet>();

        if (rooms == null)
        {
            throw new InvalidOperationException("Could not get act RoomSet.");
        }

        rooms.Ancient = chosenAncient;
    }

    public static Rng CreateVoteResolutionRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_ancient_ban_vote_act_{nextActIndex}");
    }

    public static Rng CreateSpawnResolutionRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_ancient_spawn_act_{nextActIndex}");
    }

    public static string DescribeAncients(IEnumerable<AncientEventModel> ancients)
    {
        return string.Join(", ", ancients.Select(a => $"{a.Id.Entry} ({a.Title.GetFormattedText()})"));
    }

    public static void LogPool(string context, IEnumerable<AncientEventModel> ancients)
    {
        GD.Print($"[ChooseTheAncient] {context}: {DescribeAncients(ancients)}");
    }
}