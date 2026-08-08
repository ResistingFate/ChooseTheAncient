using System;
using System.Collections.Generic;
using System.Globalization;
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
using ChooseTheAncient.ChooseTheAncientCode.Compatibility;
using ChooseTheAncient.ChooseTheAncientCode.Interop;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class ChooseTheAncientHelpers
{
    private const string RitsuAncientActValidityInterfaceName =
        "STS2RitsuLib.Scaffolding.Content.IModAncientActValidity";

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
        public required int RunModifierIndex { get; init; }
    }

    private static string GetModifierIdForDiagnostics(ModifierModel modifier)
    /*
     * Reads a stable modifier id for logs without requiring CTA to know the modifier type.
     */
    {
        string? entry = modifier.Id?.Entry;
        return string.IsNullOrWhiteSpace(entry)
            ? modifier.GetType().Name
            : entry;
    }

    public static RunState? GetRunState(RunManager runManager)
    /*
     * Reads the active RunState from RunManager through reflection.
     */
    {
        return Traverse.Create(runManager)
            .Property("State")
            .GetValue<RunState>();
    }

    private static ActModel? GetActModelForTargetIndex(RunState runState, int targetActIndex)
    /*
     * Resolves an act model for helpers that receive an absolute target act index.
     * Longer act lists use the direct index; looping/infinite act lists can reuse the finite act model list by modulo.
     */
    {
        if (targetActIndex < 0 || runState.Acts.Count == 0)
            return null;

        int resolvedIndex = targetActIndex < runState.Acts.Count
            ? targetActIndex
            : targetActIndex % runState.Acts.Count;

        return runState.Acts[resolvedIndex];
    }

    private static bool IsAncientValidForAct(AncientEventModel ancient, ActModel act)
    /*
     * Respects RitsuLib's interface-based act validity and BaseLib's optional IsValidForAct hook
     * without taking a compile-time dependency on either library.
     */
    {
        bool? ritsuValidity = InvokeRitsuAncientActValidityIfPresent(ancient, act);
        if (ritsuValidity == false)
            return false;

        if (ritsuValidity.HasValue && !IsBaseLibCustomAncient(ancient))
            return true;

        return InvokeAncientBoolHookOrDefault(
            ancient,
            "IsValidForAct",
            [typeof(ActModel)],
            [act],
            fallback: true);
    }

    private static bool? InvokeRitsuAncientActValidityIfPresent(
        AncientEventModel ancient,
        ActModel act)
    /*
     * Invokes RitsuLib's IModAncientActValidity through its interface MethodInfo.
     * Calling through the interface also supports explicit interface implementations whose runtime
     * method name is not the plain "IsValidForAct" searched by the generic hook helper.
     */
    {
        Type? validityInterface = ancient
            .GetType()
            .GetInterfaces()
            .FirstOrDefault(type => string.Equals(
                type.FullName,
                RitsuAncientActValidityInterfaceName,
                StringComparison.Ordinal));

        if (validityInterface == null)
            return null;

        MethodInfo? method = validityInterface.GetMethod(
            "IsValidForAct",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(ActModel)],
            modifiers: null);

        if (method == null || method.ReturnType != typeof(bool))
        {
            ModLog.Warn(
                $"Ignoring malformed RitsuLib ancient act-validity interface on {ancient.GetType().FullName}; " +
                "expected bool IsValidForAct(ActModel).");
            return true;
        }

        try
        {
            return (bool)method.Invoke(ancient, [act])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            Exception inner = ex.InnerException;

            ModLog.Warn(
                $"RitsuLib IsValidForAct failed for {ancient.Id.Entry} ({ancient.GetType().FullName}) " +
                $"in {act.Id.Entry}: {inner.GetType().Name}: {inner.Message}. Treating the ancient as valid.");
            return true;
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                $"Could not invoke RitsuLib IsValidForAct for {ancient.Id.Entry} ({ancient.GetType().FullName}) " +
                $"in {act.Id.Entry}: {ex.GetType().Name}: {ex.Message}. Treating the ancient as valid.");
            return true;
        }
    }

    private static bool InvokeAncientBoolHookOrDefault(
        AncientEventModel ancient,
        string hookName,
        Type[] parameterTypes,
        object?[] arguments,
        bool fallback)
    /*
     * Invokes optional ancient hooks through one validated bool-returning path.
     */
    {
        MethodInfo? method = ancient.GetType().GetMethod(
            hookName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        if (method == null)
            return fallback;

        if (method.ReturnType != typeof(bool))
        {
            ModLog.Warn(
                $"Ignoring {hookName} on {ancient.GetType().FullName} because it returns {method.ReturnType.FullName}; expected System.Boolean.");
            return fallback;
        }

        try
        {
            object? result = method.Invoke(ancient, arguments);
            if (result is bool value)
                return value;

            ModLog.Warn(
                $"Ignoring {hookName} on {ancient.GetType().FullName} because it returned {result?.GetType().FullName ?? "<null>"}; expected System.Boolean.");
            return fallback;
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to call {hookName} on {ancient.GetType().FullName}: {UnwrapReflectionException(ex)}");
            return fallback;
        }
    }

    private static bool IsBaseLibCustomAncient(AncientEventModel ancient)
    /*
     * Detects BaseLib CustomAncientModel instances without taking a compile-time dependency on BaseLib.
     */
    {
        for (Type? type = ancient.GetType(); type != null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, "BaseLib.Abstracts.CustomAncientModel", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool ShouldForceSpawnForAct(
        AncientEventModel ancient,
        ActModel targetAct,
        AncientEventModel? rngChosenAncient)
    /*
     * Calls BaseLib's ShouldForceSpawn hook by reflection so forced custom ancients keep priority on the CTA ballot.
     * Missing hooks are treated as not forced.
     */
    {
        return InvokeAncientBoolHookOrDefault(
            ancient,
            "ShouldForceSpawn",
            [typeof(ActModel), typeof(AncientEventModel)],
            [targetAct, rngChosenAncient],
            fallback: false);
    }

    private static List<AncientEventModel> GetBaseLibForcedAncientsForTargetAct(ActModel targetAct)
    /*
     * Collects BaseLib force-spawn candidates independently of IsValidForAct.
     */
    {
        AncientEventModel? rngChosenAncient = TryGetChosenAncient(targetAct);
        if (rngChosenAncient == null)
            return new List<AncientEventModel>();

        List<AncientEventModel> forcedAncients = ModelDb.AllSharedAncients
            .Where(IsBaseLibCustomAncient)
            .Where(ancient => ShouldForceSpawnForAct(ancient, targetAct, rngChosenAncient))
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        if (forcedAncients.Count > 0)
        {
            LogPool(
                $"BaseLib custom ancients requesting forced spawn for existing ancient room in {targetAct.Id.Entry}",
                forcedAncients);
        }

        return forcedAncients;
    }



    public static Rng CreateRunScopedRng(RunState runState, params object?[] streamParts)
    /*
     * Creates a deterministic CTA RNG stream from the run seed without consuming mutable run RNG state.
     */
    {
        if (streamParts.Length == 0)
            throw new ArgumentException("At least one RNG stream part is required.", nameof(streamParts));

        return SeedCompatibility.CreateNamedRng(
            SeedCompatibility.GetRunSeed(runState),
            BuildRngStreamName(streamParts));
    }

    public static string BuildRngStreamName(params object?[] streamParts)
    /*
     * Builds CTA RNG stream names through one formatting path so new streams keep the same prefix and deterministic culture.
     */
    {
        return "choose_the_ancient_" + string.Join("_", streamParts.Select(FormatRngStreamPart));
    }

    private static string FormatRngStreamPart(object? streamPart)
    /*
     * Formats a single RNG stream-name part without culture-specific number formatting.
     */
    {
        return streamPart switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => streamPart.ToString() ?? string.Empty
        };
    }


    public static List<AncientEventModel> BuildCandidatePool(
        ActModel act,
        RunState runState,
        int targetActIndex,
        IReadOnlyList<int>? enabledSourceActsOverride = null,
        IReadOnlyDictionary<string, bool>? specialAncientOverridesOverride = null,
        bool? enableRedundantSettingsOverride = null)
    /*
     * Builds the full CTA candidate pool for a target act using the configured source acts and special ancient overrides.
     */
    {
        ChooseTheAncientConfig.RefreshFromNativeSettings();

        bool enableRedundantSettings = enableRedundantSettingsOverride
            ?? ChooseTheAncientConfig.EnableRedundantSettings;
        List<AncientEventModel> forcedAncients = GetBaseLibForcedAncientsForTargetAct(act);

        if (!enableRedundantSettings)
        {
            ModLog.Info(
                $"Redundant legacy ancient settings are disabled for act {targetActIndex + 1}; " +
                "using the normal target-act pool without source-act filters or Neow/Darv overrides.");

            return BuildDefaultCandidatePool(
                act,
                runState,
                targetActIndex,
                forcedAncients,
                specialAncientOverrides: null);
        }

        IReadOnlyDictionary<string, bool> effectiveSpecialAncientOverrides = specialAncientOverridesOverride
            ?? ChooseTheAncientConfig.GetSpecialAncientOverridesSnapshot(targetActIndex);

        ModLog.Debug(
            $"BuildCandidatePool start: targetActIndex={targetActIndex + 1}, " +
            $"targetAct={act.Id.Entry}, currentActIndex={runState.CurrentActIndex + 1}, " +
            $"enableRedundantSettings={enableRedundantSettings}, " +
            $"enabledSourceActsOverride={(enabledSourceActsOverride == null ? "<null>" : ChooseTheAncientConfig.DescribeAncientPoolSourceActs(enabledSourceActsOverride))}, " +
            $"localSourceActs={ChooseTheAncientConfig.DescribeAncientPoolSourceActs(ChooseTheAncientConfig.GetEnabledAncientPoolSourceActs(targetActIndex))}, " +
            $"effectiveSpecialAncientOverrides={ChooseTheAncientConfig.DescribeSpecialAncientOverrides(effectiveSpecialAncientOverrides)}");

        List<AncientEventModel> defaultPool = BuildDefaultCandidatePool(
            act,
            runState,
            targetActIndex,
            forcedAncients,
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
            forcedAncients,
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
        IReadOnlyList<AncientEventModel> forcedAncients,
        IReadOnlyDictionary<string, bool>? specialAncientOverrides)
    /*
     * Builds the vanilla-like candidate pool for the target act, then applies CTA's special ancient overrides.
     */
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
            .Concat(forcedAncients)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Id.Entry)
            .ToList();

        LogPool($"Act {targetActIndex + 1} default pool before special overrides for target {targetAct.Id.Entry}", defaultPool);

        if (specialAncientOverrides != null)
        {
            defaultPool = ApplySpecialAncientOverrides(
                targetAct,
                runState,
                targetActIndex,
                defaultPool,
                specialAncientOverrides);
        }
        else
        {
            if (targetActIndex == 0)
            {
                IReadOnlyDictionary<string, int>? weights = AncientConfigsPlusInterop.TryParseWeights(1);
                if (weights == null ||
                    !weights.TryGetValue("Darv", out int darvWeight) ||
                    darvWeight <= 0)
                {
                    defaultPool.RemoveAll(IsDarvAncient);
                }
            }

            ModLog.Debug(
                $"Skipping legacy Neow/Darv special overrides for act {targetActIndex + 1} " +
                "because redundant settings are disabled.");
        }

        LogPool($"Act {runState.CurrentActIndex + 1} default pool for target {targetAct.Id.Entry}", defaultPool);
        return defaultPool;
    }

    private static List<AncientEventModel> BuildConfiguredCandidatePool(
        ActModel targetAct,
        RunState runState,
        int targetActIndex,
        IReadOnlyList<int> enabledSourceActs,
        IReadOnlyList<AncientEventModel> forcedAncients,
        IReadOnlyDictionary<string, bool> specialAncientOverrides)
    /*
     * Builds a candidate pool from the user-selected source acts and shared ancients that are valid for the target act.
     */
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
        configuredPool.AddRange(forcedAncients);

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
    /*
     * Collects shared ancients, including BaseLib custom shared ancients, that are valid for the target act.
     */
    {
        if (!runState.UnlockState.SharedAncients.Any())
            ModLog.Debug("runState.UnlockState.SharedAncients is empty");

        /*
         * BaseLib registers custom ancients through ModelDb.AllSharedAncients and later injects them
         * into an act's shared ancient subset while that act is generating rooms. CTA builds its ballot
         * before it hands control back to vanilla EnterNextAct, so make sure BaseLib custom ancients are
         * visible here even if they have not yet been copied into UnlockState.SharedAncients.
         */
        List<AncientEventModel> allSharedAncients = runState.UnlockState.SharedAncients
            .Concat(ModelDb.AllSharedAncients.Where(IsBaseLibCustomAncient))
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
    /*
     * Applies CTA's hard-coded special ancient toggles to add or remove special ancients from a candidate pool.
     */
    {
        List<AncientEventModel> adjustedPool = pool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry)
            .ToList();

        if (targetActIndex >= 3)
        {
            ModLog.Debug(
                $"Skipping legacy Neow/Darv special overrides for extended act position {targetActIndex + 1}. " +
                "The candidate pool will be left unchanged.");
            return adjustedPool;
        }

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
    /*
     * Applies one special ancient toggle by removing a disabled ancient or locating and adding an enabled one.
     */
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
    /*
     * Returns whether a named special ancient override is enabled in the resolved override map.
     */
    {
        return specialAncientOverrides.TryGetValue(ancientId, out bool enabled) && enabled;
    }

    private static AncientEventModel? TryFindAncientForOverride(
        RunState runState,
        ActModel targetAct,
        string ancientId,
        Func<AncientEventModel, bool> matcher)
    /*
     * Searches every known ancient for a special override target, preferring a candidate valid for the target act.
     */
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
    /*
     * Enumerates shared and act-specific ancients known to the current run state.
     */
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
    /*
     * Reduces the full candidate pool to the CTA ballot, then uniformly shuffles every displayed ancient.
     * BaseLib custom ancients can still reserve inclusion slots, but custom/vanilla status no longer affects display position.
     */
    {
        ModLog.Info(
            $"CTA ballot uniform-v3 active for act {nextActIndex + 1}; requestedCount={ancientCount}, poolCount={pool.Count}.");
        ModLog.Debug(
            $"LimitCandidatePoolForVote start for act {nextActIndex + 1}: requestedCount={ancientCount}, poolCount={pool.Count}, pool={DescribeAncients(pool)}");

        if (pool.Count == 0 || ancientCount <= 0)
        {
            ModLog.Debug(
                $"Returning an empty CTA ballot for act {nextActIndex + 1} because poolCount={pool.Count}, requestedCount={ancientCount}.");
            return new List<AncientEventModel>();
        }

        List<AncientEventModel> distinctPool = pool
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry, StringComparer.Ordinal)
            .ToList();

        IReadOnlyDictionary<string, int>? ancientConfigsPlusWeights =
            AncientConfigsPlusInterop.TryParseWeights(nextActIndex + 1);

        if (nextActIndex == 0 &&
            ChooseTheAncientConfig.EnableRedundantSettings &&
            ancientConfigsPlusWeights is { Count: > 0 })
        {
            Dictionary<string, int> adjustedWeights =
                new(ancientConfigsPlusWeights, StringComparer.Ordinal)
                {
                    ["Darv"] = ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("DARV", 0) ? 1 : 0
                };

            ancientConfigsPlusWeights = adjustedWeights;
        }

        bool useAncientConfigsPlusWeights =
            ancientConfigsPlusWeights is { Count: > 0 };

        if (useAncientConfigsPlusWeights)
        {
            List<AncientEventModel> weightedPool = FilterCandidatePoolWithAncientConfigsPlusWeights(
                runState,
                nextActIndex,
                distinctPool,
                ancientConfigsPlusWeights!);

            if (weightedPool.Count == 0)
            {
                ModLog.Warn(
                    $"AncientConfigsPlus has weights for act {nextActIndex + 1}, but every CTA candidate had weight 0 or no configured weight. " +
                    "Returning an empty ballot so CTA's existing fallback path can handle this safely.");
                return new List<AncientEventModel>();
            }

            distinctPool = weightedPool;
        }
        else if (ancientConfigsPlusWeights != null)
        {
            ModLog.Debug(
                $"AncientConfigsPlus returned no configured weights for act {nextActIndex + 1}; CTA will use its normal uniform ballot limiting.");
        }

        ancientCount = Math.Min(ancientCount, distinctPool.Count);

        string candidatePoolSignature = BuildAncientIdSignature(distinctPool);
        List<AncientEventModel> includedAncients;

        if (distinctPool.Count <= ancientCount)
        {
            ModLog.Debug(
                $"Including all {distinctPool.Count} candidate(s) for act {nextActIndex + 1} because requestedCount={ancientCount}; " +
                "the full candidate set will still be uniformly shuffled for display.");
            includedAncients = distinctPool;
        }
        else if (useAncientConfigsPlusWeights)
        {
            includedAncients = SelectAncientsForLimitedBallotWithAncientConfigsPlusWeights(
                runState,
                nextActIndex,
                distinctPool,
                ancientConfigsPlusWeights!,
                ancientCount,
                candidatePoolSignature);
        }
        else
        {
            includedAncients = SelectAncientsForLimitedBallot(
                runState,
                nextActIndex,
                distinctPool,
                ancientCount,
                candidatePoolSignature);
        }

        LogPool($"Act {nextActIndex + 1} included CTA ballot before display shuffle", includedAncients);

        List<AncientEventModel> displayOrder = ShuffleBallotAncients(
            runState,
            nextActIndex,
            includedAncients,
            ancientCount,
            candidatePoolSignature,
            "display");

        LogPool($"Act {nextActIndex + 1} uniformly shuffled CTA ballot display order", displayOrder);
        return displayOrder;
    }

    private static List<AncientEventModel> FilterCandidatePoolWithAncientConfigsPlusWeights(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> collectedPool,
        IReadOnlyDictionary<string, int> weights)
    /*
     * Applies AncientConfigsPlus enable/disable semantics after CTA has collected its valid candidates.
     * ACP keys ancients by runtime type name and treats missing keys as weight 0.
     */
    {
        List<AncientConfigsPlusCandidate<AncientEventModel>> candidates =
            BuildAncientConfigsPlusWeightCandidates(runState, nextActIndex, collectedPool);

        List<AncientEventModel> filteredPool =
            AncientConfigsPlusWeightingCore.FilterCandidatesWithPositiveWeights(candidates, weights);

        ModLog.Info(
            $"AncientConfigsPlus filtered Act {nextActIndex + 1} CTA candidates from {collectedPool.Count} to {filteredPool.Count} " +
            "using positive configured weights.");

        LogPool($"Act {nextActIndex + 1} AncientConfigsPlus-positive CTA candidates", filteredPool);
        return filteredPool;
    }

    private static List<AncientEventModel> SelectAncientsForLimitedBallotWithAncientConfigsPlusWeights(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> distinctPool,
        IReadOnlyDictionary<string, int> weights,
        int ancientCount,
        string candidatePoolSignature)
    /*
     * Uses AncientConfigsPlus weights for CTA ballot inclusion while leaving CTA's display-order shuffle unchanged.
     */
    {
        Rng inclusionRng = CreateRunScopedRng(
            runState,
            "ballot",
            nextActIndex + 1,
            ancientCount,
            candidatePoolSignature,
            "ancient_configs_plus_weighted_inclusion");

        List<AncientConfigsPlusCandidate<AncientEventModel>> candidates =
            BuildAncientConfigsPlusWeightCandidates(runState, nextActIndex, distinctPool);

        List<AncientEventModel> includedAncients =
            AncientConfigsPlusWeightingCore.SelectWeightedBallotWithoutReplacement(
                candidates,
                weights,
                ancientCount,
                inclusionRng.NextInt);

        LogPool($"Act {nextActIndex + 1} AncientConfigsPlus-weighted CTA ballot included ancients", includedAncients);
        return includedAncients;
    }

    private static List<AncientConfigsPlusCandidate<AncientEventModel>> BuildAncientConfigsPlusWeightCandidates(
        RunState runState,
        int nextActIndex,
        IEnumerable<AncientEventModel> collectedPool)
    /*
     * Converts collected CTA ancient candidates into the small testable shape used by the ACP weighting core.
     */
    {
        ActModel? targetAct = GetActModelForTargetIndex(runState, nextActIndex);

        AncientEventModel? rngChosenAncient = targetAct == null
            ? null
            : TryGetChosenAncient(targetAct);

        List<AncientConfigsPlusCandidate<AncientEventModel>> candidates = new();

        foreach (AncientEventModel ancient in collectedPool)
        {
            candidates.Add(new AncientConfigsPlusCandidate<AncientEventModel>(
                ancient,
                ancient.Id.Entry,
                ancient.GetType().Name,
                targetAct != null &&
                rngChosenAncient != null &&
                ShouldForceSpawnForAct(ancient, targetAct, rngChosenAncient)));
        }

        return candidates;
    }


    private static List<AncientEventModel> SelectAncientsForLimitedBallot(
        RunState runState,
        int nextActIndex,
        List<AncientEventModel> distinctPool,
        int ancientCount,
        string candidatePoolSignature)
    /*
     * Chooses which ancients make the ballot when more candidates exist than display slots.
     * Only ancients whose BaseLib ShouldForceSpawn hook returns true reserve slots.
     * All other vanilla and custom ancients compete through the same randomized inclusion order.
     */
    {
        List<AncientEventModel> inclusionOrder = ShuffleBallotAncients(
            runState,
            nextActIndex,
            distinctPool,
            ancientCount,
            candidatePoolSignature,
            "inclusion");

        LogPool($"Act {nextActIndex + 1} randomized CTA ballot inclusion order", inclusionOrder);

        ActModel? targetAct = GetActModelForTargetIndex(runState, nextActIndex);

        if (targetAct == null)
        {
            ModLog.Warn(
                $"Could not resolve target act {nextActIndex + 1} while limiting the CTA ballot; " +
                "falling back to the first entries from the randomized inclusion order.");
            return inclusionOrder
                .Take(ancientCount)
                .ToList();
        }

        AncientEventModel? rngChosenAncient = TryGetChosenAncient(targetAct);

        List<AncientEventModel> forceSpawnAncients = rngChosenAncient == null
            ? new List<AncientEventModel>()
            : inclusionOrder
                .Where(ancient => ShouldForceSpawnForAct(ancient, targetAct, rngChosenAncient))
                .DistinctBy(ancient => ancient.Id)
                .ToList();

        if (forceSpawnAncients.Count > 0)
        {
            LogPool(
                $"Act {nextActIndex + 1} BaseLib custom ancients requesting forced spawn",
                forceSpawnAncients);
        }

        HashSet<string> selectedIds = new(StringComparer.Ordinal);

        foreach (AncientEventModel forced in forceSpawnAncients)
        {
            if (selectedIds.Count >= ancientCount)
                break;

            selectedIds.Add(forced.Id.Entry);
        }

        if (selectedIds.Count > 0)
        {
            ModLog.Info(
                $"Reserved {selectedIds.Count} of {ancientCount} Act {nextActIndex + 1} CTA ballot slot(s) " +
                $"for ancient(s) requesting forced spawn: {string.Join(",", selectedIds)}.");
        }

        foreach (AncientEventModel ancient in inclusionOrder)
        {
            if (selectedIds.Count >= ancientCount)
                break;

            selectedIds.Add(ancient.Id.Entry);
        }

        List<AncientEventModel> includedAncients = distinctPool
            .Where(ancient => selectedIds.Contains(ancient.Id.Entry))
            .ToList();

        LogPool($"Act {nextActIndex + 1} limited CTA ballot included ancients", includedAncients);
        return includedAncients;
    }

    private static AncientEventModel? TryGetChosenAncient(ActModel act)
    /*
     * Reads the act's currently RNG-chosen ancient so BaseLib ShouldForceSpawn checks receive the same context vanilla passes.
     */
    {
        try
        {
            RoomSet? rooms = Traverse.Create(act)
                .Field("_rooms")
                .GetValue<RoomSet>();

            if (rooms == null || !rooms.HasAncient)
                return null;

            return rooms.Ancient;
        }
        catch (Exception ex)
        {
            ModLog.Debug($"Could not read the act's current RNG-chosen ancient while prioritizing CTA custom ancients: {ex.GetType().Name}");
            return null;
        }
    }

    public static void SetChosenAncient(ActModel act, AncientEventModel chosenAncient)
    /*
     * Replaces the target act's selected ancient room model with the ancient chosen by CTA.
     */
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
    /*
     * Reads the target act's currently selected ancient room model and throws if no ancient is present.
     */
    {
        RoomSet? rooms = Traverse.Create(act)
            .Field("_rooms")
            .GetValue<RoomSet>();

        if (rooms == null || !rooms.HasAncient)
        {
            throw new InvalidOperationException("Could not get the act's current ancient.");
        }

        return rooms.Ancient;
    }

    public static AncientEventModel ResolveVanillaAct1FallbackAncient(ActModel act, RunState runState)
    /*
     * Finds the vanilla Act 1 ancient to use when CTA cannot present a valid Act 1 ballot.
     */
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
    /*
     * Forces the current run into an Act 1 ancient start state for CTA's starting-room replacement.
     */
    {
        runState.ExtraFields.StartedWithNeow = true;
    }


    public static List<ModifierBootstrapAction> BuildModifierBootstrapActions(Player player)
    /*
     * Builds deferred actions that apply start-of-run custom-game modifier options before CTA resolves Act 1.
     */
    {
        RunState runState = player.RunState as RunState
            ?? throw new InvalidOperationException("Player is not attached to a mutable RunState.");

        EventModel syntheticNeow = CreateSyntheticNeowForModifierBootstrap(player, runState);
        List<ModifierBootstrapAction> actions = new();

        IReadOnlyList<ModifierModel> modifiers = runState.Modifiers;

        for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
        {
            ModifierModel modifier = modifiers[modifierIndex];
            string modifierId = GetModifierIdForDiagnostics(modifier);

            Func<Task>? applyAsync = modifier.GenerateNeowOption(syntheticNeow);
            if (applyAsync == null)
            {
                ModLog.Debug(
                    $"Modifier {modifierId}@{modifierIndex} did not provide a Neow bootstrap action; skipping.");
                continue;
            }

            actions.Add(new ModifierBootstrapAction
            {
                Modifier = modifier,
                ApplyAsync = applyAsync,
                RunModifierIndex = modifierIndex
            });

            ModLog.Debug(
                $"Queued Neow bootstrap action for modifier {modifierId}@{modifierIndex} " +
                "through the generic GenerateNeowOption path.");
        }

        return actions;
    }

    private static EventModel CreateSyntheticNeowForModifierBootstrap(Player player, RunState runState)
    /*
     * Creates a temporary Neow event instance used only to ask modifiers for their bootstrap actions.
     */
    {
        AncientEventModel syntheticNeow = (AncientEventModel)ModelDb.AncientEvent<Neow>().ToMutable();
        EventOwnerBackingField.SetValue(syntheticNeow, player);

        ulong bootstrapSeed = ComputeVanillaEventSeedForCurrentGame(runState, player, syntheticNeow);
        Rng bootstrapRng = SeedCompatibility.CreateRng(bootstrapSeed);
        EventRngBackingField.SetValue(syntheticNeow, bootstrapRng);

        syntheticNeow.CalculateVars();

        ModLog.Debug(
            $"Created synthetic Neow for modifier bootstrap with seed {bootstrapSeed} " +
            $"for player {player.NetId}.");

        return syntheticNeow;
    }

    public static bool IsNeowAncient(AncientEventModel ancient)
    /*
     * Identifies Neow by type or model ID across vanilla and reflected contexts.
     */
    {
        return ancient is Neow
               || string.Equals(ancient.Id.Entry, nameof(Neow), StringComparison.OrdinalIgnoreCase)
               || string.Equals(ancient.Id.Entry, "NEOW", StringComparison.OrdinalIgnoreCase);
    }


    public static bool ShouldResetHpBeforeAncientHeal(AncientEventModel ancient)
    /*
     * Replaces vanilla's Neow-only HP reset condition before ancient healing runs.
     * The Act 1 starting ancient always gets the start-of-run HP baseline, while Neow in later CTA acts heals like any other ancient.
     */
    {
        RunState? runState = ancient.Owner?.RunState as RunState;
        if (runState == null)
            return IsNeowAncient(ancient);

        bool shouldReset = IsAct1StartingMapPoint(runState);

        if (shouldReset)
        {
            ModLog.Info(
                $"Applying Act 1 starting HP baseline through vanilla ancient heal reset for {ancient.Id.Entry}.");
        }
        else if (IsNeowAncient(ancient))
        {
            ModLog.Info(
                $"Skipping vanilla Neow HP reset outside Act 1 start so {ancient.Id.Entry} heals like a normal ancient. " +
                $"Act={runState.CurrentActIndex + 1}.");
        }

        return shouldReset;
    }


    public static bool IsDarvAncient(AncientEventModel ancient)
    /*
     * Identifies Darv by type or model ID for CTA's special ancient override.
     */
    {
        return string.Equals(ancient.Id.Entry, "DARV", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ancient.GetType().Name, "Darv", StringComparison.OrdinalIgnoreCase);
    }


    private static Rng CreateBallotShuffleRng(
        RunState runState,
        int nextActIndex,
        int ancientCount,
        string candidatePoolSignature,
        string purpose)
    /*
     * Creates an isolated deterministic RNG stream for CTA ballot inclusion or display shuffling.
     * Including the candidate-pool signature prevents unrelated pool changes from reusing the same permutation stream.
     */
    {
        return CreateRunScopedRng(
            runState,
            "ballot_uniform_v3",
            purpose,
            "act",
            nextActIndex,
            "count",
            ancientCount,
            "pool",
            candidatePoolSignature);
    }

    private static List<AncientEventModel> ShuffleBallotAncients(
        RunState runState,
        int nextActIndex,
        IEnumerable<AncientEventModel> ancients,
        int ancientCount,
        string candidatePoolSignature,
        string purpose)
    /*
     * Uniformly shuffles a distinct set of ancients with Fisher-Yates while starting from a stable ID-sorted order.
     * This gives every included ancient the same chance at every display slot without consuming mutable run RNG state.
     */
    {
        List<AncientEventModel> shuffled = ancients
            .DistinctBy(ancient => ancient.Id)
            .OrderBy(ancient => ancient.Id.Entry, StringComparer.Ordinal)
            .ToList();

        Rng rng = CreateBallotShuffleRng(
            runState,
            nextActIndex,
            ancientCount,
            candidatePoolSignature,
            purpose);

        rng.Shuffle(shuffled);
        return shuffled;
    }

    private static string BuildAncientIdSignature(IEnumerable<AncientEventModel> ancients)
    /*
     * Creates a stable ID signature for the candidate set so deterministic shuffle streams change when the candidate set changes.
     */
    {
        return string.Join(
            "|",
            ancients
                .Select(ancient => ancient.Id.Entry)
                .OrderBy(id => id, StringComparer.Ordinal));
    }

    public static Rng CreateFinalVoteResolutionRng(RunState runState, int nextActIndex)
    /*
     * Creates the deterministic RNG used to resolve tied final votes in a host/client-safe way.
     */
    {
        return CreateRunScopedRng(
            runState,
            "final_vote",
            "act",
            nextActIndex);
    }

    public static Rng CreateSecondRoundPresentationRng(RunState runState, int nextActIndex)
    /*
     * Creates the deterministic RNG used to choose second-round preview suppression presentation details.
     */
    {
        return CreateRunScopedRng(
            runState,
            "second_vote_presentation",
            "act",
            nextActIndex);
    }
    

    public static uint ComputeVanillaEventSeed(RunState runState, Player player, EventModel eventModel)
    {
        return unchecked((uint)ComputeVanillaEventSeedForCurrentGame(runState, player, eventModel));
    }

    private static ulong ComputeVanillaEventSeedForCurrentGame(
        RunState runState,
        Player player,
        EventModel eventModel)
    /*
     * Mirrors the active game's EventModel.BeginEvent seed formula.
     * STS2 0.107.1 uses a 32-bit run seed/hash; STS2 0.109.0 beta uses 64-bit values.
     */
    {
        int ownerContribution = eventModel.IsShared ? 0 : runState.GetPlayerSlotIndex(player);
        ulong runSeed = SeedCompatibility.GetRunSeed(runState);

        if (SeedCompatibility.Uses64BitSeeds)
        {
            return unchecked(
                runSeed
                + (ulong)ownerContribution
                + SeedCompatibility.GetDeterministicHash64(eventModel.Id.Entry));
        }

        return unchecked(
            (uint)runSeed
            + (uint)ownerContribution
            + SeedCompatibility.GetDeterministicHash32(eventModel.Id.Entry));
    }

    public static Rng CreatePreviewEventRng(RunState runState, Player player, EventModel eventModel)
    /*
     * Creates the vanilla event RNG used when simulating reward previews or modifier bootstrap events that should not use CTA reward offsets.
     */
    {
        return SeedCompatibility.CreateRng(
            ComputeVanillaEventSeedForCurrentGame(runState, player, eventModel));
    }

    private static ulong ComputeAncientEventSeedForTargetAct(
        RunState runState,
        Player player,
        EventModel eventModel,
        int targetActIndex)
    /*
     * Computes CTA's event seed at the width used by the active game branch.
     * Offset zero exactly matches vanilla; earlier/later appearances add the signed act offset.
     */
    {
        ulong seed = ComputeVanillaEventSeedForCurrentGame(runState, player, eventModel);
        int normalActIndex = GetNormalMinimumActIndexForAncient(runState, eventModel);
        int actOffset = targetActIndex - normalActIndex;

        return SeedCompatibility.AddSignedOffset(seed, actOffset);
    }

    public static Rng CreateAncientEventRngForTargetAct(
        RunState runState,
        Player player,
        EventModel eventModel,
        int targetActIndex)
    /*
     * Creates the RNG CTA should use for ancient reward previews or reveals in a target act.
     */
    {
        return SeedCompatibility.CreateRng(
            ComputeAncientEventSeedForTargetAct(
                runState,
                player,
                eventModel,
                targetActIndex));
    }

    public static int GetRewardActOffsetForTargetAct(
        RunState runState,
        EventModel eventModel,
        int targetActIndex)
    /*
     * Returns how far the target act is from the ancient's normal minimum act.
     * Zero means vanilla reward RNG should be preserved.
     */
    {
        int normalActIndex = GetNormalMinimumActIndexForAncient(runState, eventModel);
        return targetActIndex - normalActIndex;
    }

    private static int GetNormalMinimumActIndexForAncient(
        RunState runState,
        EventModel eventModel)
    /*
     * Finds the earliest act where an ancient belongs under normal act-specific/shared validity rules.
     * CTA source-act settings do not affect this calculation.
     */
    {
        if (string.Equals(eventModel.Id.Entry, "DARV", StringComparison.OrdinalIgnoreCase))
            return 1;

        int? minimumActIndex = null;

        for (int actIndex = 0; actIndex < runState.Acts.Count; actIndex++)
        {
            ActModel act = runState.Acts[actIndex];

            if (act.GetUnlockedAncients(runState.UnlockState)
                .Any(ancient => AncientIdsMatch(ancient, eventModel)))
            {
                minimumActIndex = MinActIndex(minimumActIndex, actIndex);
            }
        }

        List<AncientEventModel> sharedAncients = runState.UnlockState.SharedAncients
            .Concat(ModelDb.AllSharedAncients.Where(ancient => AncientIdsMatch(ancient, eventModel) || IsBaseLibCustomAncient(ancient)))
            .DistinctBy(ancient => ancient.Id)
            .ToList();

        AncientEventModel? sharedAncient = sharedAncients
            .FirstOrDefault(ancient => AncientIdsMatch(ancient, eventModel));

        if (sharedAncient == null &&
            eventModel is AncientEventModel ancientEventModel &&
            IsBaseLibCustomAncient(ancientEventModel))
        {
            sharedAncient = ancientEventModel;
        }

        if (sharedAncient != null)
        {
            for (int actIndex = 0; actIndex < runState.Acts.Count; actIndex++)
            {
                if (IsAncientValidForAct(sharedAncient, runState.Acts[actIndex]))
                {
                    minimumActIndex = MinActIndex(minimumActIndex, actIndex);
                    break;
                }
            }
        }

        if (minimumActIndex.HasValue)
            return minimumActIndex.Value;

        int fallbackActIndex = Math.Clamp(runState.CurrentActIndex, 0, Math.Max(0, runState.Acts.Count - 1));
        ModLog.Warn(
            $"Could not determine normal minimum act for ancient {eventModel.Id.Entry}; " +
            $"falling back to current act {fallbackActIndex + 1} so CTA reward RNG remains vanilla.");

        return fallbackActIndex;
    }

    private static bool AncientIdsMatch(EventModel left, EventModel right)
    /*
     * Compares event IDs by entry so mutable preview copies and canonical models match reliably.
     */
    {
        return string.Equals(left.Id.Entry, right.Id.Entry, StringComparison.Ordinal);
    }

    private static int MinActIndex(int? existing, int candidate)
    /*
     * Updates a nullable minimum act index without allocating helper collections.
     */
    {
        return existing.HasValue ? Math.Min(existing.Value, candidate) : candidate;
    }

    public static bool TryApplyCtaAncientRewardActOffsetRng(AncientEventModel ancient, string context)
    /*
     * Applies CTA's act-offset reward RNG to a real ancient immediately before its options are generated.
     */
    {
        try
        {
            Player? owner = ancient.Owner;
            if (owner == null)
            {
                ModLog.Debug(
                    $"CTA act-offset reward RNG skipped for {ancient.Id.Entry}: ancient has no owner. Context={context}.");
                return false;
            }

            RunState? runState = owner.RunState as RunState;
            if (runState == null)
            {
                ModLog.Debug(
                    $"CTA act-offset reward RNG skipped for {ancient.Id.Entry}: owner RunState is not a mutable RunState. " +
                    $"RuntimeType={owner.RunState?.GetType().FullName ?? "null"}, Context={context}.");
                return false;
            }

            int targetActIndex = runState.CurrentActIndex;
            int normalActIndex = GetNormalMinimumActIndexForAncient(runState, ancient);
            int actOffset = targetActIndex - normalActIndex;

            if (actOffset == 0)
            {
                ModLog.Debug(
                    $"CTA reward RNG for {ancient.Id.Entry} at act {targetActIndex + 1} remains vanilla. " +
                    $"NormalAct={normalActIndex + 1}, ActOffset=0, Context={context}.");
                return false;
            }

            ulong seed = ComputeAncientEventSeedForTargetAct(
                runState,
                owner,
                ancient,
                targetActIndex);
            Rng rng = SeedCompatibility.CreateRng(seed);

            EventRngBackingField.SetValue(ancient, rng);

            ModLog.Info(
                $"Applied CTA act-offset ancient reward RNG for {ancient.Id.Entry}. " +
                $"TargetAct={targetActIndex + 1}, NormalAct={normalActIndex + 1}, ActOffset={actOffset}, Seed={seed}, Context={context}.");

            return true;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not apply CTA act-offset ancient reward RNG for {ancient.Id.Entry}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static Dictionary<string, AncientPreviewData> BuildPreviewDataByAncientId(
        Player player,
        IEnumerable<AncientEventModel> ancients,
        int nextActIndex)
    /*
     * Generates preview data for every displayed ancient and indexes successful previews by ancient ID.
     */
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
    /*
     * Simulates an ancient event at the target act and returns the reward options that should later be revealed.
     */
    {
        /* simulate the next act, and what the relic options are going to be.*/ 
        try
        {
            AncientEventModel previewEvent = (AncientEventModel)ancient.ToMutable();
            if (player.RunState is not RunState runState)
            {
                ModLog.Warn($"Could not generate preview data for ancient {ancient.Id.Entry}: player is not attached to a mutable RunState.");
                return null;
            }

            int actOffset = GetRewardActOffsetForTargetAct(runState, previewEvent, nextActIndex);
            int originalActIndex = runState.CurrentActIndex;

            var playerRngSnapshot = player.PlayerRng.ToSerializable();
            var playerOddsSnapshot = player.PlayerOdds.ToSerializable();
            var runRngSnapshot = runState.Rng.ToSerializable();
            var runOddsSnapshot = runState.Odds.ToSerializable();
            try
            {
                runState.CurrentActIndex = nextActIndex;
                EventOwnerBackingField.SetValue(previewEvent, player);
                ulong previewSeed = ComputeAncientEventSeedForTargetAct(
                    runState,
                    player,
                    previewEvent,
                    nextActIndex);
                ResetPreviewEventRng(
                    runState,
                    player,
                    previewEvent,
                    nextActIndex);
                ModLog.Debug(
                    $"Generating preview data for {ancient.Id.Entry} with preview seed {previewSeed} for player {player.NetId} " +
                    $"at act index {nextActIndex}. ActOffset={actOffset}.");

                // This is what BeginEvents does in Megacritic EventModel
                previewEvent.CalculateVars();

                IReadOnlyList<EventOption> options = GeneratePreviewOptionsRobustly(
                    player,
                    runState,
                    previewEvent,
                    ancient,
                    nextActIndex);

                LogPreviewOptions(previewEvent, ancient, options);

                return new AncientPreviewData
                {
                    PreviewEvent = previewEvent,
                    Options = options.ToList(),
                };
            }
            finally
            {
                player.PlayerRng.LoadFromSerializable(playerRngSnapshot);
                player.PlayerOdds.LoadFromSerializable(playerOddsSnapshot);
                runState.Rng.LoadFromSerializable(runRngSnapshot);

                runState.Odds.UnknownMapPoint.MonsterOdds = runOddsSnapshot.UnknownMapPointMonsterOddsValue;
                runState.Odds.UnknownMapPoint.EliteOdds = runOddsSnapshot.UnknownMapPointEliteOddsValue;
                runState.Odds.UnknownMapPoint.TreasureOdds = runOddsSnapshot.UnknownMapPointTreasureOddsValue;
                runState.Odds.UnknownMapPoint.ShopOdds = runOddsSnapshot.UnknownMapPointShopOddsValue;

                runState.CurrentActIndex = originalActIndex;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error($"Failed to generate preview data for ancient {ancient.Id.Entry}: {ex}");
            return null;
        }
    }

    private static IReadOnlyList<EventOption> GeneratePreviewOptionsRobustly(
        Player player,
        RunState runState,
        AncientEventModel previewEvent,
        AncientEventModel sourceAncient,
        int nextActIndex)
    /*
     * Attempts preview option generation through vanilla wrapper and direct concrete methods, masking Neow modifiers when needed.
     */
    {
        bool isNeowPreview = IsNeowAncient(previewEvent);
        IReadOnlyList<ModifierModel>? originalModifiers = null;
        bool maskedModifiers = false;

        try
        {
            if (isNeowPreview && runState.Modifiers.Count > 0)
            {
                originalModifiers = runState.Modifiers;
                maskedModifiers = TrySetRunStateModifiers(runState, Array.Empty<ModifierModel>());
                if (maskedModifiers)
                {
                    ModLog.Info(
                        $"Temporarily masked {originalModifiers.Count} run modifier(s) while generating the Neow preview for act {nextActIndex + 1}. " +
                        "This forces Neow to build blessing reward options instead of custom-game modifier bootstrap options.");
                }
            }

            IReadOnlyList<EventOption> options = TryGeneratePreviewOptionsViaWrapper(
                player,
                runState,
                previewEvent,
                sourceAncient,
                nextActIndex,
                requireRelicReward: isNeowPreview,
                attemptName: maskedModifiers ? "wrapper with modifiers masked" : "wrapper");

            if (HasUsablePreviewOptions(options, requireRelicReward: isNeowPreview))
                return options;

            ModLog.Warn(
                $"Preview option generation for {sourceAncient.Id.Entry} returned no usable reward options via the wrapper " +
                $"at act {nextActIndex + 1}; attempting the concrete GenerateInitialOptions method directly.");

            options = TryGeneratePreviewOptionsDirectly(
                player,
                runState,
                previewEvent,
                sourceAncient,
                nextActIndex,
                requireRelicReward: isNeowPreview);

            if (HasUsablePreviewOptions(options, requireRelicReward: isNeowPreview))
                return options;

            ModLog.Warn(
                $"Preview option generation for {sourceAncient.Id.Entry} still returned no usable reward options " +
                $"at act {nextActIndex + 1}; could not predict the rewards so will show none.");

            return Array.Empty<EventOption>();
        }
        finally
        {
            if (maskedModifiers)
            {
                TrySetRunStateModifiers(runState, originalModifiers ?? Array.Empty<ModifierModel>());
                ModLog.Debug("Restored RunState.Modifiers after robust preview generation.");
            }
        }
    }

    private static IReadOnlyList<EventOption> TryGeneratePreviewOptionsViaWrapper(
        Player player,
        RunState runState,
        AncientEventModel previewEvent,
        AncientEventModel sourceAncient,
        int nextActIndex,
        bool requireRelicReward,
        string attemptName)
    /*
     * Runs AncientEventModel.GenerateInitialOptionsWrapper for preview generation and validates the returned options.
     */
    {
        try
        {
            ResetPreviewEventRng(
                runState,
                player,
                previewEvent,
                nextActIndex);
            IReadOnlyList<EventOption> options = InvokeEventOptionGenerator(
                GenerateInitialOptionsWrapperMethod,
                previewEvent,
                $"Preview generation attempt '{attemptName}' for {sourceAncient.Id.Entry}");

            if (!HasUsablePreviewOptions(options, requireRelicReward))
            {
                ModLog.Warn(
                    $"Preview generation attempt '{attemptName}' for {sourceAncient.Id.Entry} produced " +
                    $"{DescribePreviewOptionCount(options)} at act {nextActIndex + 1}.");
            }

            return options.Where(option => option != null).ToList();
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                $"Preview generation attempt '{attemptName}' for {sourceAncient.Id.Entry} failed " +
                $"at act {nextActIndex + 1}: {UnwrapReflectionException(ex)}");
            return Array.Empty<EventOption>();
        }
    }

    private static IReadOnlyList<EventOption> TryGeneratePreviewOptionsDirectly(
        Player player,
        RunState runState,
        AncientEventModel previewEvent,
        AncientEventModel sourceAncient,
        int nextActIndex,
        bool requireRelicReward)
    /*
     * Invokes the concrete GenerateInitialOptions method as a fallback when the wrapper produces no usable preview rewards.
     */
    {
        try
        {
            ResetPreviewEventRng(
                runState,
                player,
                previewEvent,
                nextActIndex);

            MethodInfo? generateInitialOptionsMethod = AccessTools.Method(previewEvent.GetType(), "GenerateInitialOptions");
            if (generateInitialOptionsMethod == null)
            {
                ModLog.Warn($"Could not locate GenerateInitialOptions on {previewEvent.GetType().FullName} while previewing {sourceAncient.Id.Entry}.");
                return Array.Empty<EventOption>();
            }

            IReadOnlyList<EventOption> cleanedOptions = InvokeEventOptionGenerator(
                generateInitialOptionsMethod,
                previewEvent,
                $"Direct GenerateInitialOptions for {sourceAncient.Id.Entry}");

            if (!HasUsablePreviewOptions(cleanedOptions, requireRelicReward))
            {
                ModLog.Warn(
                    $"Direct GenerateInitialOptions for {sourceAncient.Id.Entry} produced " +
                    $"{DescribePreviewOptionCount(cleanedOptions)} at act {nextActIndex + 1}.");
            }

            return cleanedOptions;
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                $"Direct GenerateInitialOptions for {sourceAncient.Id.Entry} failed " +
                $"at act {nextActIndex + 1}: {UnwrapReflectionException(ex)}");
            return Array.Empty<EventOption>();
        }
    }

    private static bool HasUsablePreviewOptions(
        IReadOnlyList<EventOption> options,
        bool requireRelicReward)
    /*
     * Checks whether generated preview options contain at least one selectable reward, optionally requiring a relic reward.
     */
    {
        return options.Count > 0
               && options.Any(option => option != null && !option.IsProceed && (!requireRelicReward || option.Relic != null));
    }

    private static string DescribePreviewOptionCount(IReadOnlyList<EventOption> options)
    /*
     * Formats a compact count summary for preview option diagnostics.
     */
    {
        int nonNullCount = options.Count(option => option != null);
        int proceedCount = options.Count(option => option != null && option.IsProceed);
        int relicCount = options.Count(option => option?.Relic != null);
        return $"{options.Count} option(s), nonNull={nonNullCount}, proceed={proceedCount}, relic={relicCount}";
    }

    private static IReadOnlyList<EventOption> InvokeEventOptionGenerator(
        MethodInfo method,
        EventModel previewEvent,
        string context)
    /*
     * Invokes an event option generator and normalizes missing or malformed results to an empty option list.
     */
    {
        object? result = method.Invoke(previewEvent, Array.Empty<object>());
        if (result is not IReadOnlyList<EventOption> options)
        {
            ModLog.Warn(
                $"{context} returned {result?.GetType().FullName ?? "<null>"} instead of IReadOnlyList<EventOption>.");
            return Array.Empty<EventOption>();
        }

        return options
            .Where(option => option != null)
            .ToList();
    }

    private static string UnwrapReflectionException(Exception ex)
    /*
     * Returns the inner exception text from reflected calls so logs show the real preview-generation failure.
     */
    {
        return ex is TargetInvocationException { InnerException: not null }
            ? ex.InnerException.ToString()
            : ex.ToString();
    }

    private static Rng ResetPreviewEventRng(
        RunState runState,
        Player player,
        EventModel previewEvent,
        int targetActIndex)
    /*
     * Recreates and assigns the preview event RNG so each generation attempt starts from the same seed.
     * Target acts at the ancient's normal minimum act match vanilla exactly; earlier/later target acts use the signed act offset.
     */
    {
        Rng previewRng = CreateAncientEventRngForTargetAct(
            runState,
            player,
            previewEvent,
            targetActIndex);

        EventRngBackingField.SetValue(previewEvent, previewRng);
        return previewRng;
    }

    private static bool TrySetRunStateModifiers(
        RunState runState,
        IReadOnlyList<ModifierModel> modifiers)
    /*
     * Temporarily replaces RunState.Modifiers through reflection while generating Neow previews.
     */
    {
        try
        {
            Traverse.Create(runState)
                .Property<IReadOnlyList<ModifierModel>>(nameof(RunState.Modifiers))
                .Value = modifiers;
            return true;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Failed to set RunState.Modifiers while generating ancient preview options: {ex}");
            return false;
        }
    }


    public static async Task WaitForProcessFramesAsync(int frameCount)
    /*
     * Waits for a number of Godot process frames without blocking the main thread.
     */
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
    /*
     * Preloads visual assets for candidate ancients so the selection screen can render them smoothly.
     */
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
    /*
     * Best-effort warms one ancient's scene/art assets and logs failures without aborting the selection flow.
     */
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



    public static bool IsStartingAncientResolved(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int actIndex)
    {
        if (actIndex < 0 || actIndex >= runState.Acts.Count)
            return false;

        if (flow.ResolvedActs.Contains(actIndex))
            return true;

        if (actIndex >= runState.MapPointHistory.Count)
            return false;

        IReadOnlyList<MapPointHistoryEntry> actHistory =
            runState.MapPointHistory[actIndex];

        if (actHistory.Count == 0)
            return false;

        MapPointHistoryEntry startingEntry = actHistory[0];
        if (startingEntry.MapPointType != MapPointType.Ancient
            || startingEntry.Rooms.Count == 0)
        {
            return false;
        }

        MapPointRoomHistoryEntry startingRoom = startingEntry.Rooms[0];
        if (startingRoom.RoomType != RoomType.Event
            || startingRoom.ModelId == null)
        {
            return false;
        }

        AncientEventModel chosenAncient;
        try
        {
            chosenAncient = GetChosenAncient(runState.Acts[actIndex]);
        }
        catch
        {
            return false;
        }

        if (startingRoom.ModelId != chosenAncient.Id)
        {
            if (!TryRecoverConsoleReplacedStartingAncient(
                    runState,
                    actIndex,
                    actHistory,
                    startingEntry,
                    chosenAncient))
            {
                return false;
            }

        }

        flow.ResolvedActs.Add(actIndex);
        ModLog.Debug(
            $"Recovered Act {actIndex + 1}'s resolved CTA starting Ancient " +
            $"{chosenAncient.Id.Entry} from saved map-point history.");

        return true;
    }

    private static bool TryRecoverConsoleReplacedStartingAncient(
        RunState runState,
        int actIndex,
        IReadOnlyList<MapPointHistoryEntry> actHistory,
        MapPointHistoryEntry startingEntry,
        AncientEventModel chosenAncient)
    {
        if (startingEntry.MapPointType != MapPointType.Ancient
            || startingEntry.Rooms.Count == 0
            || startingEntry.Rooms[0].RoomType != RoomType.Event
            || startingEntry.Rooms[0].ModelId == null)
        {
            return false;
        }

        bool hasMatchingConsoleAncientEntry = actHistory
            .Skip(1)
            .Any(entry =>
                entry.MapPointType == MapPointType.Ancient
                && entry.Rooms.Any(room =>
                    room.RoomType == RoomType.Event
                    && room.ModelId == chosenAncient.Id));

        if (!hasMatchingConsoleAncientEntry)
            return false;

        if (!RewriteStartingMapPointHistoryToAncient(
                runState,
                actIndex,
                chosenAncient))
        {
            return false;
        }

        ModLog.Debug(
            $"Recovered Act {actIndex + 1}'s CTA starting Ancient after a " +
            $"console Ancient replacement. Canonical row-0 history now uses " +
            $"{chosenAncient.Id.Entry}.");
        return true;
    }


    public static bool ShouldPrepareUnresolvedStartingAncientNode(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int actIndex)
    /*
     * Keeps the Ancient node unresolved until that act's ballot
     * has been applied. ctaact. 
     */
    {
        if (actIndex < 0 || actIndex >= runState.Acts.Count)
            return false;

        bool resolved =
            IsStartingAncientResolved(runState, flow, actIndex);

        if (actIndex == 0)
            return !resolved
                   && runState.ExtraFields.StartedWithNeow;

        if (!resolved)
            return true;

        return flow.ConsoleNavigationInProgress
               && flow.ActiveFlowTargetActIndex == actIndex;
    }


    public static bool ApplyChosenAncientIconToStartingMapPoint(
        RunState runState,
        AncientEventModel chosenAncient)
    /*
     * Replaces CTA's unresolved random Ancient textures on the already-created
     * native map node without rebuilding the map screen.
     */
    {
        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen == null)
            return false;

        MapCoord startingCoord = runState.Map.StartingMapPoint.coord;
        bool updated = ApplyChosenAncientIconRecursive(
            mapScreen,
            startingCoord,
            chosenAncient,
            runState.Act.MapBgColor);

        if (updated)
        {
            mapScreen.RefreshAllPointVisuals();
        }

        return updated;
    }


    private static bool ApplyChosenAncientIconRecursive(
        Node parent,
        MapCoord startingCoord,
        AncientEventModel chosenAncient,
        Color mapBackgroundColor)
    {
        bool updated = false;

        foreach (Node child in parent.GetChildren())
        {
            if (child is NAncientMapPoint ancientMapPoint
                && ancientMapPoint.Point.coord == startingCoord)
            {
                TextureRect? icon =
                    ancientMapPoint.GetNodeOrNull<TextureRect>("Icon");
                TextureRect? outline =
                    ancientMapPoint.GetNodeOrNull<TextureRect>("Icon/Outline");

                if (icon != null)
                {
                    icon.Texture = chosenAncient.MapIcon;
                }

                if (outline != null)
                {
                    outline.Texture = chosenAncient.MapIconOutline;
                    outline.Modulate = mapBackgroundColor;
                }

                updated = icon != null || outline != null;
            }

            updated |= ApplyChosenAncientIconRecursive(
                child,
                startingCoord,
                chosenAncient,
                mapBackgroundColor);
        }

        return updated;
    }


    public static bool IsCurrentActStartingMapPoint(RunState runState)
    /*
     * Checks whether the run is currently inside the active act's starting map point.
     * Act 1 additionally requires the vanilla Neow-start route that CTA replaces.
     */
    {
        int actIndex = runState.CurrentActIndex;
        if (actIndex < 0 || actIndex >= runState.Acts.Count)
            return false;

        if (actIndex == 0 && !runState.ExtraFields.StartedWithNeow)
            return false;

        MapCoord? currentCoord = runState.CurrentMapCoord;
        return currentCoord.HasValue
               && currentCoord.Value == runState.Map.StartingMapPoint.coord;
    }

    public static bool IsAct1StartingMapPoint(RunState runState)
    {
        return runState.CurrentActIndex == 0
               && IsCurrentActStartingMapPoint(runState);
    }

    public static bool ShouldUseStartingAncientShell(
        RunState runState,
        ChooseTheAncientFlowState flow)
    /*
     * Determines whether CTA should replace the current act's unresolved. Act 1 keeps its Neow-start guard;
     * Acts 2+ use the same shell after the generated map auto-enters its first node.
     */
    {
        int actIndex = runState.CurrentActIndex;
        if (actIndex < 0 || actIndex >= runState.Acts.Count)
            return false;

        if (IsStartingAncientResolved(runState, flow, actIndex))
            return false;

        if (actIndex == 0 && !runState.ExtraFields.StartedWithNeow)
            return false;

        return true;
    }

    public static bool ShouldUseAct1StartShell(
        RunState runState,
        ChooseTheAncientFlowState flow)
    {
        return runState.CurrentActIndex == 0
               && ShouldUseStartingAncientShell(runState, flow);
    }

    public static void ConvertStartingShellToChosenAncient(
        RunState runState,
        AncientEventModel chosenAncient)
    /*
     * Rewrites the current act's shell-room history and native starting map node
     * into the chosen Ancient after CTA resolves the room-owned ballot.
     */
    {
        runState.Map.StartingMapPoint.PointType = MapPointType.Ancient;
        RewriteCurrentMapPointHistoryToAncient(runState, chosenAncient);

        NMapScreen? mapScreen = NMapScreen.Instance;
        if (mapScreen != null)
        {
            SeedCompatibility.SetMap(
                mapScreen,
                runState.Map,
                SeedCompatibility.GetRunSeed(runState),
                clearDrawings: true);

            bool iconUpdated =
                ApplyChosenAncientIconToStartingMapPoint(
                    runState,
                    chosenAncient);

            if (!iconUpdated)
            {
                ModLog.Debug(
                    $"Could not immediately refresh the resolved starting Ancient " +
                    $"map icon to {chosenAncient.Id.Entry}. The next map rebuild " +
                    $"will use the chosen Ancient's native textures.");
            }
        }
    }

    public static void ConvertAct1StartShellToChosenAncient(
        RunState runState,
        AncientEventModel chosenAncient)
    {
        ConvertStartingShellToChosenAncient(runState, chosenAncient);
    }

    public static void RewriteCurrentMapPointHistoryToAncient(
        RunState runState,
        AncientEventModel chosenAncient)
    /*
     * Rewrites run-history entries for the current map point so history reflects the chosen ancient instead of the shell.
     */
    {
        MapPointHistoryEntry? entry = runState.CurrentMapPointHistoryEntry;
        if (entry == null)
            return;

        RewriteMapPointHistoryEntryToAncient(entry, chosenAncient);
    }

    public static bool RewriteStartingMapPointHistoryToAncient(
        RunState runState,
        int actIndex,
        AncientEventModel chosenAncient)
    /*
     * Keeps the ancient node icon chosen if the game is reloaded.
     */
    {
        if (actIndex < 0 || actIndex >= runState.MapPointHistory.Count)
            return false;

        IReadOnlyList<MapPointHistoryEntry> actHistory =
            runState.MapPointHistory[actIndex];

        if (actHistory.Count == 0)
            return false;

        MapPointHistoryEntry startingEntry = actHistory[0];

        if (startingEntry.MapPointType != MapPointType.Ancient)
            return false;

        RewriteMapPointHistoryEntryToAncient(
            startingEntry,
            chosenAncient);
        return true;
    }

    private static void RewriteMapPointHistoryEntryToAncient(
        MapPointHistoryEntry entry,
        AncientEventModel chosenAncient)
    {
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

    // Log stuff below

    private static string SafeFormatLoc(LocString? loc)
    /*
     * Formats a localized string for logs while tolerating null or formatting failures.
     */
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
    /*
     * Logs generated preview options with enough detail to compare predicted rewards against the later event.
     */
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
    /*
     * Formats a comma-separated list of ancient IDs for diagnostic logs.
     */
    {
        return string.Join(", ", ancients.Select(a => $"{a.Id.Entry} ({a.Title.GetFormattedText()})"));
    }

    public static void LogPool(string context, IEnumerable<AncientEventModel> ancients)
    /*
     * Logs a named ancient pool using the shared ancient-list formatter.
     */
    {
        if (!ModLog.IsDebugEnabled)
            return;

        ModLog.Debug($"{context}: {DescribeAncients(ancients)}");
    }
    
    
}
