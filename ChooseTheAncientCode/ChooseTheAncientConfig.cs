using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChooseTheAncient.Scripts;
using Godot;
using SysEnv = System.Environment;

namespace ChooseTheAncient.ChooseTheAncientCode;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3,
    Trace = 4
}

internal static class ModLog
{
    private const string Prefix = "[ChooseTheAncient]";
    private const string ConfigFileName = "ChooseTheAncient.logconfig.cfg";
    private const string EnvVarName = "CHOOSETHEANCIENT_LOG_LEVEL";

    public static LogLevel CurrentLevel { get; private set; } = LogLevel.Info;
    public static string CurrentLevelSource { get; private set; } = "default";

    private sealed class LogConfigFile
    {
        public string? LogLevel { get; set; }
    }

    static ModLog()
    {
        string? rawLevel = TryReadLogLevelFromConfigFile();
        if (TryParseLogLevel(rawLevel, out LogLevel configLevel))
        {
            CurrentLevel = configLevel;
            CurrentLevelSource = "config";
        }

        string? envLevel = SysEnv.GetEnvironmentVariable(EnvVarName);
        if (TryParseLogLevel(envLevel, out LogLevel envParsed))
        {
            CurrentLevel = envParsed;
            CurrentLevelSource = "env";
        }

        AnnounceActiveLevel();
    }

    public static void SetLevel(LogLevel level, string source = "runtime")
    {
        if (CurrentLevel == level && string.Equals(CurrentLevelSource, source, StringComparison.Ordinal))
            return;

        LogLevel previousLevel = CurrentLevel;
        string previousSource = CurrentLevelSource;

        CurrentLevel = level;
        CurrentLevelSource = source;
        AnnounceActiveLevel(previousLevel, previousSource);
    }

    public static bool IsDebugEnabled => CurrentLevel >= LogLevel.Debug;
    public static bool IsTraceEnabled => CurrentLevel >= LogLevel.Trace;

    public static void Error(string message) => Write(LogLevel.Error, message, isError: true);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Trace(string message) => Write(LogLevel.Trace, message);

    private static void Write(LogLevel level, string message, bool isError = false)
    {
        if (level > CurrentLevel)
            return;

        WriteAlways(level, message, isError);
    }

    private static void AnnounceActiveLevel(LogLevel? previousLevel = null, string? previousSource = null)
    {
        string message = previousLevel is null || previousSource is null
            ? $"[Startup] Active log level: {CurrentLevel} (source={CurrentLevelSource})."
            : $"[Startup] Active log level: {CurrentLevel} (source={CurrentLevelSource}, previous={previousLevel}/{previousSource}).";

        WriteAlways(CurrentLevel, message, isError: CurrentLevel == LogLevel.Error);
    }

    private static void WriteAlways(LogLevel level, string message, bool isError = false)
    {
        string line = $"{Prefix} [{level}] {message}";
        if (isError)
            GD.PrintErr(line);
        else
            GD.Print(line);
    }

    private static string? TryReadLogLevelFromConfigFile()
    {
        try
        {
            string assemblyLocation = typeof(ModLog).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyLocation))
                return null;

            string? assemblyFolder = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrWhiteSpace(assemblyFolder))
                return null;

            string configPath = Path.Combine(assemblyFolder, ConfigFileName);
            if (!File.Exists(configPath))
                return null;

            string raw = File.ReadAllText(configPath).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (raw.StartsWith("{"))
            {
                LogConfigFile? parsed = JsonSerializer.Deserialize<LogConfigFile>(raw);
                return parsed?.LogLevel;
            }

            return raw;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseLogLevel(string? rawLevel, out LogLevel level)
    {
        if (!string.IsNullOrWhiteSpace(rawLevel) && Enum.TryParse(rawLevel, true, out LogLevel parsed))
        {
            level = parsed;
            return true;
        }

        level = default;
        return false;
    }
}

internal static class ChooseTheAncientConfig
{
    public enum VoteClickTargetMode
    {
        ButtonOnly = 0,
        WholeCard = 1,
        WholeSlot = 2,
    }

    public enum SelectionGameMode
    {
        MontyHall = 0,
        FairFight = 1,
        WantToKnowEverything = 2,
        SimplePicker = 3,
    }

    public const int DefaultAncientCount = 3;
    public const bool DefaultShowControllerHotkeys = false;
    public const bool DefaultShowOnlyButtonOutline = false;
    public const VoteClickTargetMode DefaultVoteClickTarget = VoteClickTargetMode.ButtonOnly;
    public const SelectionGameMode DefaultSelectionGameMode = SelectionGameMode.MontyHall;
    public const LogLevel DefaultLogLevel = LogLevel.Info;

    public static readonly string[] VoteClickTargetOptions =
    {
        "Button only",
        "Whole card",
        "Whole ancient slot"
    };

    public static readonly string[] LogLevelOptions =
    {
        nameof(LogLevel.Error),
        nameof(LogLevel.Warn),
        nameof(LogLevel.Info),
        nameof(LogLevel.Debug),
        nameof(LogLevel.Trace)
    };

    public static readonly string[] SelectionGameModeOptions =
    {
        "Monty Hall",
        "Fair Fight",
        "I Want To Know Everything",
        "Simple Picker"
    };

    public static int AncientCount { get; private set; } = DefaultAncientCount;
    public static bool ShowControllerHotkeys { get; private set; } = DefaultShowControllerHotkeys;
    public static bool ShowOnlyButtonOutline { get; private set; } = DefaultShowOnlyButtonOutline;
    public static VoteClickTargetMode VoteClickTarget { get; private set; } = DefaultVoteClickTarget;
    public static SelectionGameMode GameMode { get; private set; } = DefaultSelectionGameMode;
    public static LogLevel CurrentLogLevel { get; private set; } = ModLog.CurrentLevel;

    private const int AncientPoolSourceActCount = 3;
    private const string NeowAncientId = "NEOW";
    private const string DarvAncientId = "DARV";

    private static readonly Dictionary<int, bool[]> AncientPoolSourceActsByTargetAct = new()
    {
        // Act 1 ancients.
        { 0, new[] { false, true, true } },

        // Act 2 ancients.
        { 1, new[] { true, true, true } },

        // Act 3 ancients.
        { 2, new[] { true, true, true } },
    };

    private static readonly Dictionary<string, bool[]> SpecialAncientOverridesByTargetAct =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { NeowAncientId, new[] { true, false, false } },
            { DarvAncientId, new[] { false, true, true } },
        };

    private static bool[] GetDefaultAncientPoolSourceActsForTargetAct(int targetActIndex)
    {
        return targetActIndex switch
        {
            0 => new[] { false, true, true },
            1 => new[] { true, true, true },
            2 => new[] { true, true, true },
            _ => new[] { true, true, true }
        };
    }

    public static bool GetDefaultAncientPoolSourceActEnabled(int targetActIndex, int sourceActIndex)
    {
        bool[] defaults = GetDefaultAncientPoolSourceActsForTargetAct(targetActIndex);
        if (sourceActIndex < 0 || sourceActIndex >= defaults.Length)
            return true;

        return defaults[sourceActIndex];
    }

    private static bool[] GetDefaultSpecialAncientOverridesForAncient(string ancientId)
    {
        return ancientId.ToUpperInvariant() switch
        {
            NeowAncientId => new[] { true, false, false },
            DarvAncientId => new[] { false, true, true },
            _ => new[] { false, false, false }
        };
    }

    public static bool GetDefaultSpecialAncientOverrideEnabled(string ancientId, int targetActIndex)
    {
        bool[] defaults = GetDefaultSpecialAncientOverridesForAncient(ancientId);
        if (targetActIndex < 0 || targetActIndex >= defaults.Length)
            return false;

        return defaults[targetActIndex];
    }

    public static bool IsSpecialAncientOverrideEnabled(string ancientId, int targetActIndex)
    {
        if (!SpecialAncientOverridesByTargetAct.TryGetValue(ancientId, out bool[]? actFlags))
            return false;

        if (targetActIndex < 0 || targetActIndex >= actFlags.Length)
            return false;

        return actFlags[targetActIndex];
    }

    public static void ApplySpecialAncientOverrideToggle(string ancientId, int targetActIndex, object value)
    {
        if (!SpecialAncientOverridesByTargetAct.TryGetValue(ancientId, out bool[]? actFlags))
        {
            ModLog.Warn($"Attempted to apply unsupported special-ancient override for '{ancientId}'.");
            return;
        }

        if (targetActIndex < 0 || targetActIndex >= actFlags.Length)
        {
            ModLog.Warn($"Attempted to apply special-ancient override for invalid act {targetActIndex + 1} on '{ancientId}'.");
            return;
        }

        actFlags[targetActIndex] = Convert.ToBoolean(value);
        ModLog.Info(
            $"Applied special ancient override: ancient={ancientId}, act={targetActIndex + 1}, enabled={actFlags[targetActIndex]}.");
    }

    public static string GetSpecialAncientOverrideConfigKey(string ancientId, int targetActIndex)
    {
        string normalizedAncientId = ancientId.ToUpperInvariant() switch
        {
            NeowAncientId => "neow",
            DarvAncientId => "darv",
            _ => ancientId.ToLowerInvariant()
        };

        return $"include{char.ToUpperInvariant(normalizedAncientId[0])}{normalizedAncientId.Substring(1)}InAct{targetActIndex + 1}Selection";
    }

    public static string GetSpecialAncientOverrideHeaderLabel(string ancientId)
    {
        return ancientId.ToUpperInvariant() switch
        {
            NeowAncientId => "Neow Overrides",
            DarvAncientId => "Darv Overrides",
            _ => $"{ancientId} Overrides"
        };
    }

    public static string GetSpecialAncientOverrideToggleLabel(int targetActIndex)
    {
        return $"Include in Act {targetActIndex + 1} selection";
    }

    public static string DescribeSpecialAncientOverrides(string ancientId)
    {
        if (!SpecialAncientOverridesByTargetAct.TryGetValue(ancientId, out bool[]? actFlags))
            return "(unsupported)";

        List<string> enabledActs = new();
        for (int targetActIndex = 0; targetActIndex < actFlags.Length; targetActIndex++)
        {
            if (actFlags[targetActIndex])
                enabledActs.Add($"Act {targetActIndex + 1}");
        }

        return enabledActs.Count == 0 ? "(none)" : string.Join(", ", enabledActs);
    }

public static IReadOnlyDictionary<string, bool> GetSpecialAncientOverridesSnapshot(int targetActIndex)
{
    Dictionary<string, bool> snapshot = new(StringComparer.OrdinalIgnoreCase)
    {
        [NeowAncientId] = IsSpecialAncientOverrideEnabled(NeowAncientId, targetActIndex),
        [DarvAncientId] = IsSpecialAncientOverrideEnabled(DarvAncientId, targetActIndex),
    };

    return snapshot;
}

public static int GetSpecialAncientOverrideMask(int targetActIndex)
{
    int mask = 0;

    if (IsSpecialAncientOverrideEnabled(NeowAncientId, targetActIndex))
        mask |= 1 << 0;

    if (IsSpecialAncientOverrideEnabled(DarvAncientId, targetActIndex))
        mask |= 1 << 1;

    return mask;
}

public static IReadOnlyDictionary<string, bool> GetSpecialAncientOverridesFromMask(int targetActIndex, int mask)
{
    Dictionary<string, bool> snapshot = new(StringComparer.OrdinalIgnoreCase)
    {
        [NeowAncientId] = (mask & (1 << 0)) != 0,
        [DarvAncientId] = (mask & (1 << 1)) != 0,
    };

    return snapshot;
}

public static string DescribeSpecialAncientOverrides(IReadOnlyDictionary<string, bool>? overrides)
{
    if (overrides == null || overrides.Count == 0)
        return "(none)";

    List<string> enabledAncients = new();

    if (overrides.TryGetValue(NeowAncientId, out bool neowEnabled) && neowEnabled)
        enabledAncients.Add(NeowAncientId);

    if (overrides.TryGetValue(DarvAncientId, out bool darvEnabled) && darvEnabled)
        enabledAncients.Add(DarvAncientId);

    return enabledAncients.Count == 0 ? "(none)" : string.Join(", ", enabledAncients);
}


    public static void RefreshFromModConfig()
    {
        AncientCount = NormalizeAncientCount(
            ModConfigBridge.GetValue("ancientCount", (float)DefaultAncientCount));
        
        ShowControllerHotkeys =
            ModConfigBridge.GetValue("showControllerHotkeys", DefaultShowControllerHotkeys);

        ShowOnlyButtonOutline =
            ModConfigBridge.GetValue("showOnlyButtonOutline", DefaultShowOnlyButtonOutline);

        object voteClickTargetValue = ModConfigBridge.GetValue<object>(
            "voteClickTarget",
            VoteClickTargetToOption(DefaultVoteClickTarget));
        VoteClickTarget = NormalizeVoteClickTarget(voteClickTargetValue);

        if (ModConfigBridge.IsAvailable && !string.Equals(
                Convert.ToString(voteClickTargetValue),
                VoteClickTargetToOption(VoteClickTarget),
                StringComparison.Ordinal))
        {
            ModConfigBridge.SetValue("voteClickTarget", VoteClickTargetToOption(VoteClickTarget));
        }

        object gameModeValue = ModConfigBridge.GetValue<object>(
            "gameMode",
            SelectionGameModeToOption(DefaultSelectionGameMode));
        GameMode = NormalizeSelectionGameMode(gameModeValue);

        if (ModConfigBridge.IsAvailable && !string.Equals(
                Convert.ToString(gameModeValue),
                SelectionGameModeToOption(GameMode),
                StringComparison.Ordinal))
        {
            ModConfigBridge.SetValue("gameMode", SelectionGameModeToOption(GameMode));
        }

        RefreshAncientPoolSourceActsFromModConfig();
        RefreshSpecialAncientOverridesFromModConfig();

        if (ModConfigBridge.IsAvailable)
        {
            object logLevelValue = ModConfigBridge.GetValue<object>(
                "logLevel",
                LogLevelToOption(ModLog.CurrentLevel));
            CurrentLogLevel = NormalizeLogLevel(logLevelValue);

            if (!string.Equals(
                    Convert.ToString(logLevelValue),
                    LogLevelToOption(CurrentLogLevel),
                    StringComparison.Ordinal))
            {
                ModConfigBridge.SetValue("logLevel", LogLevelToOption(CurrentLogLevel));
            }

            ModLog.SetLevel(CurrentLogLevel, "modconfig");
        }
        else
        {
            CurrentLogLevel = ModLog.CurrentLevel;
        }

        ModLog.Info(
            "Config refresh complete. " +
            $"AncientCount={AncientCount}, GameMode={GameMode}, " +
            $"Act1Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(0))}, " +
            $"Act2Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(1))}, " +
            $"Act3Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(2))}, " +
            $"NeowOverrides={DescribeSpecialAncientOverrides(NeowAncientId)}, " +
            $"DarvOverrides={DescribeSpecialAncientOverrides(DarvAncientId)}.");
    }

    public static void ApplyAncientCount(object value)
    {
        AncientCount = NormalizeAncientCount(value);
    }

    public static void ApplyShowControllerHotkeys(object value)
    {
        ShowControllerHotkeys = Convert.ToBoolean(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
    }

    public static void ApplyShowOnlyButtonOutlineHotkeys(object value)
    {
        ShowOnlyButtonOutline = Convert.ToBoolean(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
    }

    public static void ApplyVoteClickTarget(object value)
    {
        VoteClickTarget = NormalizeVoteClickTarget(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
    }

    public static void ApplySelectionGameMode(object value)
    {
        GameMode = NormalizeSelectionGameMode(value);
    }

    public static bool HasAncientPoolSourceActConfig(int targetActIndex)
    {
        return AncientPoolSourceActsByTargetAct.ContainsKey(targetActIndex);
    }

    public static IReadOnlyList<int> GetEnabledAncientPoolSourceActs(int targetActIndex)
    {
        if (!AncientPoolSourceActsByTargetAct.TryGetValue(targetActIndex, out bool[]? sourceActFlags))
            return Array.Empty<int>();

        return GetEnabledAncientPoolSourceActs(sourceActFlags);
    }

    public static IReadOnlyList<int> GetEnabledAncientPoolSourceActsFromMask(int targetActIndex, int sourceActMask)
    {
        if (!HasAncientPoolSourceActConfig(targetActIndex))
            return Array.Empty<int>();

        bool[] decodedFlags = DecodeAncientPoolSourceActMask(sourceActMask);
        return GetEnabledAncientPoolSourceActs(decodedFlags);
    }

    public static int GetAncientPoolSourceActMask(int targetActIndex)
    {
        if (!AncientPoolSourceActsByTargetAct.TryGetValue(targetActIndex, out bool[]? sourceActFlags))
            return GetDefaultAncientPoolSourceActMask();

        return EncodeAncientPoolSourceActMask(sourceActFlags);
    }

    public static string DescribeAncientPoolSourceActs(IEnumerable<int> enabledSourceActs)
    {
        List<int> enabledSourceActList = enabledSourceActs
            .Distinct()
            .OrderBy(sourceActIndex => sourceActIndex)
            .ToList();

        if (enabledSourceActList.Count == 0)
            return "(none)";

        return string.Join(", ", enabledSourceActList.Select(sourceActIndex => GetAncientPoolSourceActLabel(sourceActIndex)));
    }

    public static void ApplyAncientPoolSourceActToggle(int targetActIndex, int sourceActIndex, object value)
    {
        if (!AncientPoolSourceActsByTargetAct.TryGetValue(targetActIndex, out bool[]? sourceActFlags))
        {
            ModLog.Warn($"Attempted to apply ancient pool toggle for unsupported target act {targetActIndex + 1}.");
            return;
        }

        if (sourceActIndex < 0 || sourceActIndex >= sourceActFlags.Length)
        {
            ModLog.Warn($"Attempted to apply ancient pool toggle for invalid source act {sourceActIndex + 1} on target act {targetActIndex + 1}.");
            return;
        }

        sourceActFlags[sourceActIndex] = Convert.ToBoolean(value);
        ModLog.Info(
            $"Applied ancient pool toggle: targetAct={targetActIndex + 1}, sourceAct={sourceActIndex + 1}, enabled={sourceActFlags[sourceActIndex]}. " +
            $"Now enabled: {DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(targetActIndex))}");
    }

    public static string GetAncientPoolSourceActConfigKey(int targetActIndex, int sourceActIndex)
    {
        return $"act{targetActIndex + 1}AncientsFromAct{sourceActIndex + 1}";
    }

    public static string GetAncientPoolTargetActLabel(int targetActIndex)
    {
        return $"Act {targetActIndex + 1} Ancients";
    }

    public static string GetAncientPoolSourceActLabel(int sourceActIndex)
    {
        return $"From Act {sourceActIndex + 1}";
    }

    public static int GetDefaultAncientPoolSourceActMask()
    {
        return (1 << AncientPoolSourceActCount) - 1;
    }

    private static List<int> GetEnabledAncientPoolSourceActs(IReadOnlyList<bool> sourceActFlags)
    {
        List<int> enabledSourceActs = new(sourceActFlags.Count);
        for (int sourceActIndex = 0; sourceActIndex < sourceActFlags.Count; sourceActIndex++)
        {
            if (sourceActFlags[sourceActIndex])
                enabledSourceActs.Add(sourceActIndex);
        }

        return enabledSourceActs;
    }

    private static int EncodeAncientPoolSourceActMask(IReadOnlyList<bool> sourceActFlags)
    {
        int encodedMask = 0;

        int sourceActCount = Math.Min(sourceActFlags.Count, AncientPoolSourceActCount);
        for (int sourceActIndex = 0; sourceActIndex < sourceActCount; sourceActIndex++)
        {
            if (sourceActFlags[sourceActIndex])
                encodedMask |= 1 << sourceActIndex;
        }

        return encodedMask;
    }

    private static bool[] DecodeAncientPoolSourceActMask(int sourceActMask)
    {
        int normalizedMask = sourceActMask & GetDefaultAncientPoolSourceActMask();
        bool[] decodedFlags = new bool[AncientPoolSourceActCount];

        for (int sourceActIndex = 0; sourceActIndex < AncientPoolSourceActCount; sourceActIndex++)
        {
            decodedFlags[sourceActIndex] = (normalizedMask & (1 << sourceActIndex)) != 0;
        }

        return decodedFlags;
    }

    public static void ApplyLogLevel(object value)
    {
        CurrentLogLevel = NormalizeLogLevel(value);
        ModLog.SetLevel(CurrentLogLevel, "modconfig");
    }

    public static string VoteClickTargetToOption(VoteClickTargetMode mode)
    {
        return mode switch
        {
            VoteClickTargetMode.ButtonOnly => VoteClickTargetOptions[0],
            VoteClickTargetMode.WholeCard => VoteClickTargetOptions[1],
            VoteClickTargetMode.WholeSlot => VoteClickTargetOptions[2],
            _ => VoteClickTargetOptions[0]
        };
    }

    public static string LogLevelToOption(LogLevel level)
    {
        return level switch
        {
            LogLevel.Error => LogLevelOptions[0],
            LogLevel.Warn => LogLevelOptions[1],
            LogLevel.Info => LogLevelOptions[2],
            LogLevel.Debug => LogLevelOptions[3],
            LogLevel.Trace => LogLevelOptions[4],
            _ => LogLevelOptions[2]
        };
    }

    public static string SelectionGameModeToOption(SelectionGameMode mode)
    {
        return mode switch
        {
            SelectionGameMode.MontyHall => SelectionGameModeOptions[0],
            SelectionGameMode.FairFight => SelectionGameModeOptions[1],
            SelectionGameMode.WantToKnowEverything => SelectionGameModeOptions[2],
            SelectionGameMode.SimplePicker => SelectionGameModeOptions[3],
            _ => SelectionGameModeOptions[0]
        };
    }

    private static void RefreshAncientPoolSourceActsFromModConfig()
    {
        foreach ((int targetActIndex, bool[] sourceActFlags) in AncientPoolSourceActsByTargetAct)
        {
            bool[] defaults = GetDefaultAncientPoolSourceActsForTargetAct(targetActIndex);

            for (int sourceActIndex = 0; sourceActIndex < sourceActFlags.Length; sourceActIndex++)
            {
                string key = GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex);
                bool fallback = sourceActIndex < defaults.Length ? defaults[sourceActIndex] : true;
                bool loaded = ModConfigBridge.GetValue(key, fallback);
                sourceActFlags[sourceActIndex] = loaded;

                ModLog.Info(
                    $"Loaded ModConfig key '{key}' = {loaded} for {GetAncientPoolTargetActLabel(targetActIndex).ToLowerInvariant()} pool.");
            }

            ModLog.Info(
                $"{GetAncientPoolTargetActLabel(targetActIndex)} sources after refresh: " +
                $"{DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(targetActIndex))}");
        }
    }

    private static void RefreshSpecialAncientOverridesFromModConfig()
    {
        foreach ((string ancientId, bool[] actFlags) in SpecialAncientOverridesByTargetAct)
        {
            bool[] defaults = GetDefaultSpecialAncientOverridesForAncient(ancientId);

            for (int targetActIndex = 0; targetActIndex < actFlags.Length; targetActIndex++)
            {
                string key = GetSpecialAncientOverrideConfigKey(ancientId, targetActIndex);
                bool fallback = targetActIndex < defaults.Length && defaults[targetActIndex];
                bool loaded = ModConfigBridge.GetValue(key, fallback);
                actFlags[targetActIndex] = loaded;

                ModLog.Info(
                    $"Loaded ModConfig key '{key}' = {loaded} for {GetSpecialAncientOverrideHeaderLabel(ancientId)}.");
            }

            ModLog.Info(
                $"{GetSpecialAncientOverrideHeaderLabel(ancientId)} after refresh: {DescribeSpecialAncientOverrides(ancientId)}");
        }
    }

    internal static SelectionGameMode NormalizeSelectionGameMode(object value)
    {
        if (value is SelectionGameMode mode)
            return mode;

        if (value is string rawString)
        {
            if (string.Equals(rawString, SelectionGameModeOptions[0], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(SelectionGameMode.MontyHall), StringComparison.OrdinalIgnoreCase))
            {
                return SelectionGameMode.MontyHall;
            }

            if (string.Equals(rawString, SelectionGameModeOptions[1], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(SelectionGameMode.FairFight), StringComparison.OrdinalIgnoreCase))
            {
                return SelectionGameMode.FairFight;
            }

            if (string.Equals(rawString, SelectionGameModeOptions[2], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(SelectionGameMode.WantToKnowEverything), StringComparison.OrdinalIgnoreCase))
            {
                return SelectionGameMode.WantToKnowEverything;
            }

            if (string.Equals(rawString, SelectionGameModeOptions[3], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(SelectionGameMode.SimplePicker), StringComparison.OrdinalIgnoreCase))
            {
                return SelectionGameMode.SimplePicker;
            }

            if (int.TryParse(rawString, out int parsedInt))
                return NormalizeSelectionGameMode(parsedInt);
        }

        int rawValue = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultSelectionGameMode
        };

        rawValue = Math.Clamp(rawValue, (int)SelectionGameMode.MontyHall, (int)SelectionGameMode.SimplePicker);
        return (SelectionGameMode)rawValue;
    }

    private static int NormalizeAncientCount(object value)
    {
        int count = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => DefaultAncientCount
        };

        return Math.Clamp(count, 2, 8);
    }

    private static VoteClickTargetMode NormalizeVoteClickTarget(object value)
    {
        if (value is VoteClickTargetMode mode)
            return mode;

        if (value is string rawString)
        {
            if (string.Equals(rawString, VoteClickTargetOptions[0], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(VoteClickTargetMode.ButtonOnly), StringComparison.OrdinalIgnoreCase))
            {
                return VoteClickTargetMode.ButtonOnly;
            }

            if (string.Equals(rawString, VoteClickTargetOptions[1], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(VoteClickTargetMode.WholeCard), StringComparison.OrdinalIgnoreCase))
            {
                return VoteClickTargetMode.WholeCard;
            }

            if (string.Equals(rawString, VoteClickTargetOptions[2], StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, nameof(VoteClickTargetMode.WholeSlot), StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawString, "Whole ancient slot", StringComparison.OrdinalIgnoreCase))
            {
                return VoteClickTargetMode.WholeSlot;
            }

            if (int.TryParse(rawString, out int parsedInt))
                return NormalizeVoteClickTarget(parsedInt);
        }

        int rawValue = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultVoteClickTarget
        };

        rawValue = Math.Clamp(rawValue, (int)VoteClickTargetMode.ButtonOnly, (int)VoteClickTargetMode.WholeSlot);
        return (VoteClickTargetMode)rawValue;
    }

    private static LogLevel NormalizeLogLevel(object value)
    {
        if (value is LogLevel level)
            return level;

        if (value is string rawString)
        {
            if (Enum.TryParse(rawString, true, out LogLevel parsed))
                return parsed;

            if (int.TryParse(rawString, out int parsedInt))
                return NormalizeLogLevel(parsedInt);
        }

        int rawValue = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultLogLevel
        };

        rawValue = Math.Clamp(rawValue, (int)LogLevel.Error, (int)LogLevel.Trace);
        return (LogLevel)rawValue;
    }
}
