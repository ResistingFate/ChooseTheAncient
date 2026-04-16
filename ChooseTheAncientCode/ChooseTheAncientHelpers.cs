using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class ChooseTheAncientHelpers
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

    public sealed class ModifierBootstrapAction
    {
        public required ModifierModel Modifier { get; init; }
        public required Func<Task> ApplyAsync { get; init; }
    }

    public static RunState? GetRunState(RunManager runManager)
    {
        return Traverse.Create(runManager)
            .Property("State")
            .GetValue<RunState>();
    }

    private static bool IsAncientValidForAct(AncientEventModel ancient, ActModel act)
    {
        /*
         * Made to handle CustomAncients in BaseLib without using BaseLib
         */
        MethodInfo? method = ancient.GetType().GetMethod(
            "IsValidForAct",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ActModel)],
            modifiers: null);

        if (method == null || method.ReturnType != typeof(bool))
            return true;

        try
        {
            return (bool)method.Invoke(ancient, [act])!;
        }
        catch (Exception e)
        {
            ModLog.Error($"Failed to call IsValidForAct on {ancient.GetType().FullName}: {e}");
            return true;
        }
    }



    public static List<AncientEventModel> BuildCandidatePool(
        ActModel act,
        RunState runState,
        int targetActIndex,
        IReadOnlyList<int>? enabledSourceActsOverride = null,
        IReadOnlyDictionary<string, bool>? specialAncientOverridesOverride = null)
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides = specialAncientOverridesOverride
            ?? ChooseTheAncientConfig.GetSpecialAncientOverridesSnapshot(targetActIndex);

        ModLog.Debug(
            $"BuildCandidatePool start: targetActIndex={targetActIndex + 1}, " +
            $"targetAct={act.Id.Entry}, currentActIndex={runState.CurrentActIndex + 1}, " +
            $"enabledSourceActsOverride={(enabledSourceActsOverride == null ? "<null>" : ChooseTheAncientConfig.DescribeAncientPoolSourceActs(enabledSourceActsOverride))}, " +
            $"localSourceActs={ChooseTheAncientConfig.DescribeAncientPoolSourceActs(ChooseTheAncientConfig.GetEnabledAncientPoolSourceActs(targetActIndex))}, " +
            $"effectiveSpecialAncientOverrides={ChooseTheAncientConfig.DescribeSpecialAncientOverrides(effectiveSpecialAncientOverrides)}");

        List<AncientEventModel> defaultPool = BuildDefaultCandidatePool(
            act,
            runState,
            targetActIndex,
            effectiveSpecialAncientOverrides);

        if (!ChooseTheAncientConfig.HasAncientPoolSourceActConfig(targetActIndex))
        {
            ModLog.Warn(
                $"Act {targetActIndex + 1} has no configured source-act row. " +
                $"Using default candidate pool for {act.Id.Entry}.");
            return defaultPool;
        }

        IReadOnlyList<int> enabledSourceActs = enabledSourceActsOverride
            ?? ChooseTheAncientConfig.GetEnabledAncientPoolSourceActs(targetActIndex);

        ModLog.Debug(
            $"{(enabledSourceActsOverride != null ? "Using override ancient pool source acts" : "Using local ancient pool source acts")} " +
            $"for act {targetActIndex + 1}: {ChooseTheAncientConfig.DescribeAncientPoolSourceActs(enabledSourceActs)}");

        if (enabledSourceActs.Count == 0)
        {
            ModLog.Warn(
                $"No ancient source acts are enabled for act {targetActIndex + 1}; " +
                $"falling back to the default pool for {act.Id.Entry}.");
            return defaultPool;
        }

        List<AncientEventModel> filteredPool = BuildConfiguredCandidatePool(
            act,
            runState,
            targetActIndex,
            enabledSourceActs,
            effectiveSpecialAncientOverrides);

        if (filteredPool.Count == 0)
        {
            ModLog.Warn(
                $"Ancient source filters removed every candidate for act {targetActIndex + 1}; " +
                $"falling back to the default pool for {act.Id.Entry}.");
            return defaultPool;
        }

        LogPool(
            $"Act {targetActIndex + 1} configured source pool " +
            $"[{ChooseTheAncientConfig.DescribeAncientPoolSourceActs(enabledSourceActs)}]",
            filteredPool);

        return filteredPool;
    }

    private static List<AncientEventModel> BuildDefaultCandidatePool(
        ActModel targetAct,
        RunState runState,
        int targetActIndex,
        IReadOnlyDictionary<string, bool> specialAncientOverrides)
    {
        List<AncientEventModel> targetActUnlockedAncients = targetAct
            .GetUnlockedAncients(runState.UnlockState)
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        LogPool($"Act {targetActIndex + 1} unlocked ancients from target act {targetAct.Id.Entry}", targetActUnlockedAncients);

        List<AncientEventModel> sharedSubset = GetSharedAncientsValidForTargetAct(targetAct, runState);

        List<AncientEventModel> defaultPool = targetActUnlockedAncients
            .Concat(sharedSubset)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.Entry)
            .ToList();

        LogPool($"Act {targetActIndex + 1} default pool before special overrides for target {targetAct.Id.Entry}", defaultPool);

        defaultPool = ApplySpecialAncientOverrides(
            targetAct,
            runState,
            targetActIndex,
            defaultPool,
            specialAncientOverrides);

        LogPool($"Act {runState.CurrentActIndex + 1} default pool for target {targetAct.Id.Entry}", defaultPool);
        return defaultPool;
    }

    private static List<AncientEventModel> BuildConfiguredCandidatePool(
        ActModel targetAct,
        RunState runState,
        int targetActIndex,
        IReadOnlyList<int> enabledSourceActs,
        IReadOnlyDictionary<string, bool> specialAncientOverrides)
    {
        List<AncientEventModel> configuredPool = new();

        foreach (int sourceActIndex in enabledSourceActs)
        {
            if (sourceActIndex < 0 || sourceActIndex >= runState.Acts.Count)
            {
                ModLog.Warn(
                    $"Configured ancient source act {sourceActIndex + 1} is out of range for run state " +
                    $"while building act {targetActIndex + 1}'s pool.");
                continue;
            }

            ActModel sourceAct = runState.Acts[sourceActIndex];
            List<AncientEventModel> rawSourceActAncients = sourceAct
                .GetUnlockedAncients(runState.UnlockState)
                .DistinctBy(ancient => ancient.Id)
                .OrderBy(ancient => ancient.Id.Entry)
                .ToList();

            LogPool(
                $"Act {targetActIndex + 1} raw source act {sourceActIndex + 1} unlocked ancients",
                rawSourceActAncients);

            List<AncientEventModel> sourceActAncients = rawSourceActAncients
                .Where(ancient => IsAncientValidForAct(ancient, targetAct))
                .ToList();

            LogPool(
                $"Act {targetActIndex + 1} source act {sourceActIndex + 1} candidates",
                sourceActAncients);

            configuredPool.AddRange(sourceActAncients);
        }

        configuredPool.AddRange(GetSharedAncientsValidForTargetAct(targetAct, runState));

        List<AncientEventModel> distinctPool = configuredPool
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.Entry)
            .ToList();

        LogPool($"Act {targetActIndex + 1} combined configured pool before special overrides", distinctPool);

        distinctPool = ApplySpecialAncientOverrides(
            targetAct,
            runState,
            targetActIndex,
            distinctPool,
            specialAncientOverrides);

        LogPool($"Act {targetActIndex + 1} combined configured pool before limiting", distinctPool);
        return distinctPool;
    }

    private static List<AncientEventModel> GetSharedAncientsValidForTargetAct(ActModel targetAct, RunState runState)
    {
        if (!runState.UnlockState.SharedAncients.Any())
            ModLog.Debug("runState.UnlockState.SharedAncients is empty");

        List<AncientEventModel> allSharedAncients = runState.UnlockState.SharedAncients
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        LogPool($"All shared ancients before validity filtering for {targetAct.Id.Entry}", allSharedAncients);

        List<AncientEventModel> sharedSubset = allSharedAncients
            .Where(ancient => IsAncientValidForAct(ancient, targetAct))
            .ToList();

        if (ModLog.IsDebugEnabled)
        {
            string sharedPool = string.Join(",", sharedSubset.Select(ancient => ancient.Id.Entry));
            ModLog.Debug($"Shared ancients valid for {targetAct.Id.Entry}: {sharedPool}");
        }

        return sharedSubset;
    }
    
    private static List<AncientEventModel> ApplySpecialAncientOverrides(
        ActModel targetAct,
        RunState runState,
        int targetActIndex,
        IEnumerable<AncientEventModel> pool,
        IReadOnlyDictionary<string, bool> specialAncientOverrides)
    {
        List<AncientEventModel> adjustedPool = pool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        LogPool($"Act {targetActIndex + 1} pool entering special overrides", adjustedPool);
        ModLog.Debug(
            $"Act {targetActIndex + 1} special override states before application: " +
            $"NEOW={ResolveSpecialAncientOverrideValue(specialAncientOverrides, "NEOW")}, " +
            $"DARV={ResolveSpecialAncientOverrideValue(specialAncientOverrides, "DARV")}");

        adjustedPool = ApplySpecialAncientOverride(
            targetAct,
            runState,
            targetActIndex,
            adjustedPool,
            "NEOW",
            IsNeowAncient,
            specialAncientOverrides);
        adjustedPool = ApplySpecialAncientOverride(
            targetAct,
            runState,
            targetActIndex,
            adjustedPool,
            "DARV",
            IsDarvAncient,
            specialAncientOverrides);

        LogPool($"Act {targetActIndex + 1} pool after special overrides", adjustedPool);
        return adjustedPool;
    }

    private static List<AncientEventModel> ApplySpecialAncientOverride(
        ActModel targetAct,
        RunState runState,
        int targetActIndex,
        List<AncientEventModel> pool,
        string ancientId,
        Func<AncientEventModel, bool> matcher,
        IReadOnlyDictionary<string, bool> specialAncientOverrides)
    {
        bool shouldInclude = ResolveSpecialAncientOverrideValue(specialAncientOverrides, ancientId);
        bool isPresent = pool.Any(matcher);

        ModLog.Debug(
            $"Evaluating special override for {ancientId} in Act {targetActIndex + 1}: " +
            $"shouldInclude={shouldInclude}, presentBefore={isPresent}, poolBefore={DescribeAncients(pool)}");

        if (!shouldInclude)
        {
            if (isPresent)
            {
                pool = pool.Where(ancient => !matcher(ancient)).ToList();
                ModLog.Info($"Removed {ancientId} from the Act {targetActIndex + 1} CTA pool due to the special override toggle.");
                LogPool($"Act {targetActIndex + 1} pool after removing {ancientId}", pool);
            }
            else
            {
                ModLog.Debug($"No removal needed for {ancientId} in Act {targetActIndex + 1}; it was already absent.");
            }

            return pool;
        }

        if (isPresent)
        {
            ModLog.Debug($"{ancientId} was already present in the Act {targetActIndex + 1} CTA pool. No addition needed.");
            return pool;
        }

        AncientEventModel? ancientToAdd = TryFindAncientForOverride(runState, targetAct, ancientId, matcher);
        if (ancientToAdd == null)
        {
            ModLog.Warn($"Could not find {ancientId} while applying the Act {targetActIndex + 1} special override.");
            return pool;
        }

        pool.Add(ancientToAdd);
        pool = pool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        ModLog.Info($"Added {ancientId} to the Act {targetActIndex + 1} CTA pool due to the special override toggle.");
        LogPool($"Act {targetActIndex + 1} pool after adding {ancientId}", pool);
        return pool;
    }


private static bool ResolveSpecialAncientOverrideValue(
    IReadOnlyDictionary<string, bool> specialAncientOverrides,
    string ancientId)
{
    return specialAncientOverrides.TryGetValue(ancientId, out bool enabled) && enabled;
}

    private static AncientEventModel? TryFindAncientForOverride(
        RunState runState,
        ActModel targetAct,
        string ancientId,
        Func<AncientEventModel, bool> matcher)
    {
        List<AncientEventModel> allKnownAncients = EnumerateAllKnownAncients(runState)
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        LogPool($"All known ancients while resolving the {ancientId} special override for {targetAct.Id.Entry}", allKnownAncients);

        AncientEventModel? validMatch = allKnownAncients
            .FirstOrDefault(ancient => matcher(ancient) && IsAncientValidForAct(ancient, targetAct));
        if (validMatch != null)
        {
            ModLog.Debug($"Resolved {ancientId} special override with valid target-act match {validMatch.Id.Entry} for {targetAct.Id.Entry}.");
            return validMatch;
        }

        AncientEventModel? anyMatch = allKnownAncients.FirstOrDefault(matcher);
        if (anyMatch != null)
        {
            ModLog.Warn(
                $"Adding {ancientId} to the CTA pool even though IsValidForAct returned false for target act {targetAct.Id.Entry}, " +
                "because the special override toggle is enabled.");
        }
        else
        {
            ModLog.Warn($"Could not find any known ancient matching {ancientId} while resolving the special override.");
        }

        return anyMatch;
    }

    private static IEnumerable<AncientEventModel> EnumerateAllKnownAncients(RunState runState)
    {
        foreach (AncientEventModel sharedAncient in runState.UnlockState.SharedAncients)
            yield return sharedAncient;

        foreach (ActModel act in runState.Acts)
        {
            foreach (AncientEventModel actAncient in act.GetUnlockedAncients(runState.UnlockState))
                yield return actAncient;
        }
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
        ModLog.Debug(
            $"LimitCandidatePoolForVote start for act {nextActIndex + 1}: requestedCount={ancientCount}, poolCount={pool.Count}, pool={DescribeAncients(pool)}");

        if (pool.Count <= ancientCount)
        {
            ModLog.Debug(
                $"Skipping ballot limiting for act {nextActIndex + 1} because poolCount={pool.Count} <= requestedCount={ancientCount}.");
            return pool;
        }

        if (pool.Count < ancientCount)
        {
            ancientCount = pool.Count;
        }

        List<AncientEventModel> shuffled = pool.ToList();
        var rng = CreateDisplayedPoolRng(runState, nextActIndex);
        rng.Shuffle(shuffled);

        LogPool($"Act {nextActIndex + 1} shuffled ballot pool", shuffled);

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


    public static AncientEventModel GetChosenAncient(ActModel act)
    {
        RoomSet? rooms = Traverse.Create(act)
            .Field("_rooms")
            .GetValue<RoomSet>();

        if (rooms?.Ancient == null)
        {
            throw new InvalidOperationException("Could not get the act's current ancient.");
        }

        return rooms.Ancient;
    }

    public static AncientEventModel ResolveVanillaAct1FallbackAncient(ActModel act, RunState runState)
    {
        try
        {
            AncientEventModel currentAncient = GetChosenAncient(act);
            if (currentAncient != null)
            {
                ModLog.Info($"Resolved Act 1 vanilla fallback ancient from the act's current ancient: {currentAncient.Id.Entry}");
                return currentAncient;
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not read the act's current ancient while resolving the Act 1 vanilla fallback: {ex.GetType().Name}");
        }

        AncientEventModel? unlockedNeow = act
            .GetUnlockedAncients(runState.UnlockState)
            .FirstOrDefault(IsNeowAncient);

        if (unlockedNeow != null)
        {
            ModLog.Info($"Resolved Act 1 vanilla fallback ancient from the target act's unlocked ancients: {unlockedNeow.Id.Entry}");
            return unlockedNeow;
        }

        AncientEventModel? sharedNeow = runState.UnlockState.SharedAncients
            .FirstOrDefault(IsNeowAncient);

        if (sharedNeow != null)
        {
            ModLog.Info($"Resolved Act 1 vanilla fallback ancient from shared ancients: {sharedNeow.Id.Entry}");
            return sharedNeow;
        }

        AncientEventModel? firstUnlocked = act
            .GetUnlockedAncients(runState.UnlockState)
            .OrderBy(ancient => ancient.Id.Entry)
            .FirstOrDefault();

        if (firstUnlocked != null)
        {
            ModLog.Warn($"Resolved Act 1 vanilla fallback ancient from the first unlocked target-act ancient: {firstUnlocked.Id.Entry}");
            return firstUnlocked;
        }

        ModLog.Warn("Resolved Act 1 vanilla fallback ancient from ModelDb.AncientEvent<Neow>().");
        return ModelDb.AncientEvent<Neow>();
    }

    public static void ForceAct1AncientStart(RunState runState)
    {
        runState.ExtraFields.StartedWithNeow = true;
    }

    public static List<AncientEventModel> PreferNonNeowAncientsForActOne(IEnumerable<AncientEventModel> pool)
    {
        return pool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();
    }

    public static List<ModifierBootstrapAction> BuildModifierBootstrapActions(Player player)
    {
        RunState runState = player.RunState as RunState
            ?? throw new InvalidOperationException("Player is not attached to a mutable RunState.");

        EventModel syntheticNeow = CreateSyntheticNeowForModifierBootstrap(player, runState);
        List<ModifierBootstrapAction> actions = new();

        foreach (ModifierModel modifier in runState.Modifiers)
        {
            Func<Task>? applyAsync = modifier.GenerateNeowOption(syntheticNeow);
            if (applyAsync == null)
                continue;

            actions.Add(new ModifierBootstrapAction
            {
                Modifier = modifier,
                ApplyAsync = applyAsync
            });
        }

        return actions;
    }

    private static EventModel CreateSyntheticNeowForModifierBootstrap(Player player, RunState runState)
    {
        AncientEventModel syntheticNeow = (AncientEventModel)ModelDb.AncientEvent<Neow>().ToMutable();
        EventOwnerBackingField.SetValue(syntheticNeow, player);

        Rng bootstrapRng = CreatePreviewEventRng(runState, player, syntheticNeow);
        EventRngBackingField.SetValue(syntheticNeow, bootstrapRng);

        syntheticNeow.CalculateVars();

        ModLog.Debug(
            $"Created synthetic Neow for modifier bootstrap with seed {bootstrapRng.Seed} " +
            $"for player {player.NetId}.");

        return syntheticNeow;
    }

    public static bool IsNeowAncient(AncientEventModel ancient)
    {
        return ancient is Neow
               || string.Equals(ancient.Id.Entry, nameof(Neow), StringComparison.OrdinalIgnoreCase)
               || string.Equals(ancient.Id.Entry, "NEOW", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDarvAncient(AncientEventModel ancient)
    {
        return string.Equals(ancient.Id.Entry, "DARV", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ancient.GetType().Name, "Darv", StringComparison.OrdinalIgnoreCase);
    }

    public static Rng CreateDisplayedPoolRng(RunState runState, int nextActIndex)
    {
        return new Rng(runState.Rng.Seed, $"choose_the_ancient_display_pool_act_{nextActIndex}");
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
    
    public static uint ComputeVanillaEventSeed(RunState runState, Player player, EventModel eventModel)
    {
        /*
         * Goal here is to copy the RNG method vanilla uses, so it'll be the same as when the ancient
         * reveals the reward
         */
        ulong ownerContribution = eventModel.IsShared ? 0UL : player.NetId;

        return unchecked((uint)(
            runState.Rng.Seed
            + ownerContribution
            + (ulong)StringHelper.GetDeterministicHashCode(eventModel.Id.Entry)));
    }

    public static Rng CreatePreviewEventRng(RunState runState, Player player, EventModel eventModel)
    {
        return new Rng(ComputeVanillaEventSeed(runState, player, eventModel));
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
                /*
                 * TODO implement patches so ancient events can be shared
                 */
                Rng previewRng = CreatePreviewEventRng(runState, player, previewEvent);
                //Rng previewRng = CreateAncientRelicOptionsRng(
                //    runState, nextActIndex, (GroupAncientOptionsPool ? 0UL : player.NetId), previewEvent.Id.Entry);
                // We use are new rng to change how the ancients randomness work and don't change it back
                EventRngBackingField.SetValue(previewEvent, previewRng);

                ModLog.Debug($"Generating preview data for {ancient.Id.Entry} with preview seed {previewRng.Seed} for player {player.NetId} at act index {nextActIndex}.");

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
            ModLog.Error($"Failed to generate preview data for ancient {ancient.Id.Entry}: {ex}");
            return null;
        }
    }


    public static async Task WaitForProcessFramesAsync(int frameCount)
    {
        SceneTree? tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return;

        int framesToWait = Math.Max(1, frameCount);
        for (int i = 0; i < framesToWait; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    public static async Task WarmAncientVisualAssetsAsync(IEnumerable<AncientEventModel> ancients)
    {
        foreach (AncientEventModel ancient in ancients
                     .DistinctBy(candidate => candidate.Id.Entry)
                     .OrderBy(candidate => candidate.Id.Entry))
        {
            TryWarmAncientVisualAssets(ancient);
            await WaitForProcessFramesAsync(1);
        }
    }

    private static void TryWarmAncientVisualAssets(AncientEventModel ancient)
    {
        try
        {
            _ = ancient.MapIcon;
            _ = ancient.MapIconOutline;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to warm map icon assets for {ancient.Id.Entry}: {ex.GetType().Name}");
        }

        try
        {
            string? scenePath = Traverse.Create(ancient)
                .Property("BackgroundScenePath")
                .GetValue<string?>();

            if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.StartsWith("res://", StringComparison.Ordinal))
            {
                return;
            }

            PackedScene? scene = GD.Load<PackedScene>(scenePath);
            if (scene == null)
            {
                ModLog.Warn($"Could not preload ancient scene for {ancient.Id.Entry} at '{scenePath}'.");
                return;
            }

            ModLog.Debug($"Preloaded ancient scene for {ancient.Id.Entry} at '{scenePath}'.");
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to warm scene assets for {ancient.Id.Entry}: {ex.GetType().Name}");
        }
    }



public static bool IsAct1StartingMapPoint(RunState runState)
{
    if (runState.CurrentActIndex != 0)
        return false;

    if (!runState.ExtraFields.StartedWithNeow)
        return false;

    MapCoord? currentCoord = runState.CurrentMapCoord;
    if (!currentCoord.HasValue)
        return false;

    return currentCoord.Value == runState.Map.StartingMapPoint.coord;
}

public static bool ShouldUseAct1StartShell(RunState runState, ChooseTheAncientFlowState flow)
{
    if (flow.ResolvedActs.Contains(0))
        return false;

    if (runState.CurrentActIndex != 0)
        return false;

    if (!runState.ExtraFields.StartedWithNeow)
        return false;

    return true;
}

public static void ConvertAct1StartShellToChosenAncient(
    RunState runState,
    AncientEventModel chosenAncient)
{
    runState.Map.StartingMapPoint.PointType = MapPointType.Ancient;
    RewriteCurrentMapPointHistoryToAncient(runState, chosenAncient);
    NMapScreen.Instance?.SetMap(runState.Map, runState.Rng.Seed, clearDrawings: true);
}

public static void RewriteCurrentMapPointHistoryToAncient(
    RunState runState,
    AncientEventModel chosenAncient)
{
    MapPointHistoryEntry? entry = runState.CurrentMapPointHistoryEntry;
    if (entry == null)
        return;

    entry.MapPointType = MapPointType.Ancient;

    if (entry.Rooms.Count == 0)
    {
        entry.Rooms.Add(new MapPointRoomHistoryEntry
        {
            RoomType = RoomType.Event,
            ModelId = chosenAncient.Id,
        });
        return;
    }

    MapPointRoomHistoryEntry room = entry.Rooms[0];
    room.RoomType = RoomType.Event;
    room.ModelId = chosenAncient.Id;
    room.MonsterIds.Clear();
    room.TurnsTaken = 0;

    while (entry.Rooms.Count > 1)
    {
        entry.Rooms.RemoveAt(entry.Rooms.Count - 1);
    }
}

    public static bool GroupAncientOptionsPool { get; set; } = false;

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
        if (!ModLog.IsDebugEnabled)
            return;

        ModLog.Debug($"Preview options for {ancient.Id.Entry}: count={options.Count}");

        if (!ModLog.IsTraceEnabled)
            return;

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

            ModLog.Trace($"  [{i}] textKey={option.TextKey}, relicId={relicId}, relicTitle={relicTitle}, title={title}, description={description}");
        }
    }

    public static string DescribeAncients(IEnumerable<AncientEventModel> ancients)
    {
        return string.Join(", ", ancients.Select(a => $"{a.Id.Entry} ({a.Title.GetFormattedText()})"));
    }

    public static void LogPool(string context, IEnumerable<AncientEventModel> ancients)
    {
        if (!ModLog.IsDebugEnabled)
            return;

        ModLog.Debug($"{context}: {DescribeAncients(ancients)}");
    }
    
    
}
