using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class AncientBanHelpers
{
    private static readonly MethodInfo GenerateInitialOptionsWrapperMethod =
        AccessTools.Method(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")
        ?? throw new InvalidOperationException("Could not locate AncientEventModel.GenerateInitialOptionsWrapper.");

    private static readonly FieldInfo EventOwnerBackingField =
        AccessTools.Field(typeof(EventModel), "<Owner>k__BackingField")
        ?? throw new InvalidOperationException("Could not locate EventModel owner backing field.");

    private static readonly FieldInfo EventRngBackingField =
        AccessTools.Field(typeof(EventModel), "<Rng>k__BackingField")
        ?? throw new InvalidOperationException("Could not locate EventModel RNG backing field.");

    public sealed class AncientPreviewData
    {
        public required AncientEventModel PreviewEvent { get; init; }
        public required IReadOnlyList<EventOption> Options { get; init; }
    }

    public static RunState? GetRunState(RunManager runManager)
    {
        return Traverse.Create(runManager)
            .Property("State")
            .GetValue<RunState>();
    }

    public static List<AncientEventModel> BuildCandidatePool(ActModel act, RunState runState)
    {
        /*
         * Makes a list of ancients with same ordering for all players based on act
         */
        List<AncientEventModel> sharedSubset = Traverse.Create(act)
            .Field("_sharedAncientSubset")
            .GetValue<List<AncientEventModel>>() ?? new List<AncientEventModel>();

        return act
            .GetUnlockedAncients(runState.UnlockState)
            .Concat(sharedSubset)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.Entry)
            .ToList();
    }

    public static List<AncientEventModel> LimitCandidatePoolForVote(
        RunState runState,
        int nextActIndex,
        List<AncientEventModel> pool, int ancientCount)
    {
        /*
         * Takes runstate, and act index, and available ancients, and number of ancients to return
         * Returns the list of ancients that will be used be the ancient ban selection screen
         */
        if (pool.Count <= ancientCount)
        {
            return pool;
        }

        if (pool.Count > ancientCount)
        {
            ancientCount = pool.Count;
        }

        List<AncientEventModel> shuffled = pool.ToList();
        var rng = CreateDisplayedPoolRng(runState, nextActIndex);
        rng.Shuffle(shuffled);

        List<AncientEventModel> limited = shuffled
            .Take(ancientCount)
            .ToList();

        LogPool($"Act {nextActIndex + 1} limited ballot", limited);
        return limited;
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

    public static Rng CreateDisplayedPoolRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_display_pool_act_{nextActIndex}");
    }

    public static Rng CreateEliminationResolutionRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_elimination_vote_act_{nextActIndex}");
    }

    public static Rng CreateFinalVoteResolutionRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_final_vote_act_{nextActIndex}");
    }

    public static Rng CreateSecondRoundPresentationRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_second_vote_presentation_act_{nextActIndex}");
    }
    
    public static Rng CreateAncientRelicOptionsRng(RunState runstate, int nextActIndex, ulong player, string ancient)
    {
        return new Rng(runstate.Rng.Seed, $"choose_the_ancient_relic_options_{nextActIndex}_{ancient}_{player}");
    }

    public static Dictionary<string, AncientPreviewData> BuildPreviewDataByAncientId(
        Player player,
        IEnumerable<AncientEventModel> ancients,
        int nextActIndex)
    {
        Dictionary<string, AncientPreviewData> previews = new();

        foreach (AncientEventModel ancient in ancients)
        {
            // Weird situations where I couldn't find the Ancient options so cased in double try block
            AncientPreviewData? preview = TryGeneratePreviewData(player, ancient, nextActIndex);
            if (preview != null)
            {
                previews[ancient.Id.Entry] = preview;
            }
        }

        return previews;
    }

    public static AncientPreviewData? TryGeneratePreviewData(
        Player player,
        AncientEventModel ancient,
        int nextActIndex)
    {
        /* simulate the next act, and what the relic options are going to be.*/ 
        try
        {
            AncientEventModel previewEvent = (AncientEventModel)ancient.ToMutable();
            RunState runState = player.RunState as RunState;
            int originalActIndex = runState.CurrentActIndex;

            try
            {
                runState.CurrentActIndex = nextActIndex;
                EventOwnerBackingField.SetValue(previewEvent, player);
                // I want to experiment with the relics rewards for all players being a shared event being a shared
                // pool. Shared pools should have the same RNG for each player, where as independent offerings
                // should have Rng based on their player ID.
                bool groupAncientOptionsPool = false;
                Rng previewRng = CreateAncientRelicOptionsRng(
                    runState, nextActIndex, (groupAncientOptionsPool ? 0UL : player.NetId), previewEvent.Id.Entry);
                // We use are new rng to change how the ancients randomness work and don't change it back
                EventRngBackingField.SetValue(previewEvent, previewRng);

                GD.Print($"[ChooseTheAncient] Generating preview data for {ancient.Id.Entry} with preview seed {previewRng.Seed} for player {player.NetId} at act index {nextActIndex}.");

                // This is what BeginEvents does in Megacritic EventModel
                previewEvent.CalculateVars();

                IReadOnlyList<EventOption> options =
                    (GenerateInitialOptionsWrapperMethod.Invoke(previewEvent, Array.Empty<object>()) as IReadOnlyList<EventOption>)
                    ?? Array.Empty<EventOption>();

                LogPreviewOptions(previewEvent, ancient, options);

                return new AncientPreviewData
                {
                    PreviewEvent = previewEvent,
                    Options = options.ToList(),
                };
            }
            finally
            {
                runState.CurrentActIndex = originalActIndex;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ChooseTheAncient] Failed to generate preview data for ancient {ancient.Id.Entry}: {ex}");
            return null;
        }
    }
    
    // Log stuff below

    private static string SafeFormatLoc(LocString? loc)
    {
        if (loc == null)
        {
            return "<null>";
        }

        try
        {
            return loc.GetFormattedText();
        }
        catch (Exception ex)
        {
            return $"<loc format failed: {ex.GetType().Name}>";
        }
    }

    private static void LogPreviewOptions(AncientEventModel previewEvent, AncientEventModel ancient, IReadOnlyList<EventOption> options)
    {
        GD.Print($"[ChooseTheAncient] Preview options for {ancient.Id.Entry}: count={options.Count}");

        for (int i = 0; i < options.Count; i++)
        {
            EventOption option = options[i];
            try
            {
                previewEvent.DynamicVars.AddTo(option.Title);
                previewEvent.DynamicVars.AddTo(option.Description);
            }
            catch
            {
            }

            string relicId = option.Relic?.Id.Entry ?? "<none>";
            string relicTitle = option.Relic != null ? SafeFormatLoc(option.Relic.Title) : "<none>";
            string title = SafeFormatLoc(option.Title);
            string description = SafeFormatLoc(option.Description);

            GD.Print($"[ChooseTheAncient]   [{i}] textKey={option.TextKey}, relicId={relicId}, relicTitle={relicTitle}, title={title}, description={description}");
        }
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
