using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChooseTheAncient.Scripts;
using Godot;
using GameLogLevel = MegaCrit.Sts2.Core.Logging.LogLevel;
using SysEnv = System.Environment;

namespace ChooseTheAncient.ChooseTheAncientCode;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3,
    VeryDebug = 4
}

public enum LogBackend
{
    BaseGame = 0,
    ModLog = 1
}

internal static class ModLog
{
    private const string Prefix = "[ChooseTheAncient]";
    private const string ConfigFileName = "ChooseTheAncient.logconfig.cfg";
    private const string EnvVarName = "CHOOSETHEANCIENT_LOG_LEVEL";

    public static LogLevel CurrentLevel { get; private set; } = LogLevel.Info;
    public static LogBackend CurrentBackend { get; private set; } = LogBackend.BaseGame;
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

        AnnounceActiveConfiguration();
    }

    public static void Configure(
        LogLevel level,
        LogBackend backend,
        string source = "runtime")
    {
        if (CurrentLevel == level &&
            CurrentBackend == backend &&
            string.Equals(CurrentLevelSource, source, StringComparison.Ordinal))
        {
            return;
        }

        LogLevel previousLevel = CurrentLevel;
        LogBackend previousBackend = CurrentBackend;
        string previousSource = CurrentLevelSource;

        CurrentLevel = level;
        CurrentBackend = backend;
        CurrentLevelSource = source;

        AnnounceActiveConfiguration(previousLevel, previousBackend, previousSource);
    }

    public static bool IsDebugEnabled =>
        CurrentBackend == LogBackend.BaseGame || CurrentLevel >= LogLevel.Debug;

    public static bool IsTraceEnabled =>
        CurrentBackend == LogBackend.BaseGame || CurrentLevel >= LogLevel.VeryDebug;

    public static bool IsVeryDebugEnabled => IsTraceEnabled;

    public static void Error(string message) => Write(LogLevel.Error, message, isError: true);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);
    // The base-game logger this level is named VeryDebug.
    public static void Trace(string message) => Write(LogLevel.VeryDebug, message);

    private static void Write(LogLevel level, string message, bool isError = false)
    {
        // Game logging forwards every message and lets the game's logger decide.
        if (CurrentBackend == LogBackend.ModLog && level > CurrentLevel)
            return;

        WriteAlways(level, message, isError);
    }

    private static void AnnounceActiveConfiguration(
        LogLevel? previousLevel = null,
        LogBackend? previousBackend = null,
        string? previousSource = null)
    {
        string current = DescribeConfiguration(
            CurrentBackend,
            CurrentLevel,
            CurrentLevelSource);

        string message = previousLevel is null ||
                         previousBackend is null ||
                         previousSource is null
            ? $"[Startup] Logging configured: {current}."
            : $"[Startup] Logging configured: {current}; previous=" +
              $"{DescribeConfiguration(previousBackend.Value, previousLevel.Value, previousSource)}.";

        WriteAlways(LogLevel.Info, message);
    }

    private static string DescribeConfiguration(
        LogBackend backend,
        LogLevel level,
        string levelSource)
    {
        return backend == LogBackend.BaseGame
            ? "mode=GameLogging, level=game setting"
            : $"mode=ModLog, level={level} (source={levelSource})";
    }

    private static void WriteAlways(
        LogLevel level,
        string message,
        bool isError = false)
    {
        if (CurrentBackend == LogBackend.BaseGame)
        {
            GameLogLevel gameLevel = level switch
            {
                LogLevel.Error => GameLogLevel.Error,
                LogLevel.Warn => GameLogLevel.Warn,
                LogLevel.Info => GameLogLevel.Info,
                LogLevel.Debug => GameLogLevel.Debug,
                LogLevel.VeryDebug => GameLogLevel.VeryDebug,
                _ => GameLogLevel.Info
            };

            MainFile.Logger.LogMessage(gameLevel, message, skipFrames: 3);
            return;
        }

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
        // Preserve compatibility with existing config files and environment values.
        if (string.Equals(rawLevel, "Trace", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.VeryDebug;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(rawLevel) &&
            Enum.TryParse(rawLevel, true, out LogLevel parsed))
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

    public const bool DefaultEnableRedundantSettings = false;
    public const int DefaultAncientCount = 3;
    public const bool DefaultShowControllerHotkeys = false;
    public const bool DefaultShowOnlyButtonOutline = true;
    public const VoteClickTargetMode DefaultVoteClickTarget = VoteClickTargetMode.WholeCard;
    public const SelectionGameMode DefaultSelectionGameMode = SelectionGameMode.MontyHall;
    public const LogLevel DefaultLogLevel = LogLevel.Info;
    public const LogBackend DefaultLogBackend = LogBackend.BaseGame;

    public static readonly string[] VoteClickTargetOptions =
    {
        "Button only",
        "Whole card",
        "Whole ancient slot"
    };

    public static readonly string[] LogBackendOptions =
    {
        "Game logging",
        "ModLog"
    };

    public static readonly string[] LogLevelOptions =
    {
        nameof(LogLevel.Error),
        nameof(LogLevel.Warn),
        nameof(LogLevel.Info),
        nameof(LogLevel.Debug),
        nameof(LogLevel.VeryDebug)
    };

    public static readonly string[] SelectionGameModeOptions =
    {
        "Monty Hall",
        "Fair Fight",
        "I Want To Know Everything",
        "Simple Picker"
    };

    private static readonly string[] VoteClickTargetOptionLocKeys =
    {
        "CHOOSETHEANCIENT.settings.option.vote_target.button_only",
        "CHOOSETHEANCIENT.settings.option.vote_target.whole_card",
        "CHOOSETHEANCIENT.settings.option.vote_target.whole_slot"
    };

    private static readonly string[] LogBackendOptionLocKeys =
    {
        "CHOOSETHEANCIENT.settings.option.log_backend.game",
        "CHOOSETHEANCIENT.settings.option.log_backend.modlog"
    };

    private static readonly string[] LogLevelOptionLocKeys =
    {
        "CHOOSETHEANCIENT.settings.option.log_level.error",
        "CHOOSETHEANCIENT.settings.option.log_level.warn",
        "CHOOSETHEANCIENT.settings.option.log_level.info",
        "CHOOSETHEANCIENT.settings.option.log_level.debug",
        "CHOOSETHEANCIENT.settings.option.log_level.very_debug"
    };

    private static readonly string[] SelectionGameModeOptionLocKeys =
    {
        "CHOOSETHEANCIENT.settings.option.game_mode.monty_hall",
        "CHOOSETHEANCIENT.settings.option.game_mode.fair_fight",
        "CHOOSETHEANCIENT.settings.option.game_mode.want_everything",
        "CHOOSETHEANCIENT.settings.option.game_mode.simple_picker"
    };

    public static bool EnableRedundantSettings { get; private set; } = DefaultEnableRedundantSettings;
    public static int AncientCount { get; private set; } = DefaultAncientCount;
    public static bool ShowControllerHotkeys { get; private set; } = DefaultShowControllerHotkeys;
    public static bool ShowOnlyButtonOutline { get; private set; } = DefaultShowOnlyButtonOutline;
    public static VoteClickTargetMode VoteClickTarget { get; private set; } = DefaultVoteClickTarget;
    public static SelectionGameMode GameMode { get; private set; } = DefaultSelectionGameMode;
    public static LogLevel CurrentLogLevel { get; private set; } = ModLog.CurrentLevel;
    public static LogBackend CurrentLogBackend { get; private set; } = ModLog.CurrentBackend;

    private const int AncientPoolSourceActCount = 3;
    private const string NeowAncientId = "NEOW";
    private const string DarvAncientId = "DARV";

    private static readonly Dictionary<string, string> _lastConfigSectionSummaries =
        new(StringComparer.Ordinal);

    private static string? _lastRefreshSummary;

    private static readonly Dictionary<int, bool[]> AncientPoolSourceActsByTargetAct = new()
    {
        { 0, new[] { true, false, false } },
        { 1, new[] { false, true, false } },
        { 2, new[] { false, false, true } },
    };

    private static readonly Dictionary<string, bool[]> SpecialAncientOverridesByTargetAct =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { NeowAncientId, new[] { true, false, false } },
            { DarvAncientId, new[] { false, true, true } },
        };

    private static bool[] GetDefaultAncientPoolSourceActsForTargetAct(int targetActIndex)
    {
        bool[] defaults = new bool[AncientPoolSourceActCount];

        if (targetActIndex >= 0 && targetActIndex < AncientPoolSourceActCount)
            defaults[targetActIndex] = true;

        return defaults;
    }

    public static bool GetDefaultAncientPoolSourceActEnabled(int targetActIndex, int sourceActIndex)
    {
        bool[] defaults = GetDefaultAncientPoolSourceActsForTargetAct(targetActIndex);
        if (sourceActIndex < 0 || sourceActIndex >= defaults.Length)
            return false;

        return defaults[sourceActIndex];
    }

    private static bool[] GetDefaultSpecialAncientOverridesForAncient(string ancientId)
    {
        return ancientId.ToUpperInvariant() switch
        {
            NeowAncientId => new[] { true, false, false },
            DarvAncientId => new[] { false, true, true },
            _ => throw new ArgumentOutOfRangeException()
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
        if (!EnableRedundantSettings)
        {
            ModLog.Debug(
                $"Ignoring special-ancient override change for '{ancientId}' because redundant settings are disabled.");
            return;
        }

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
        PersistCurrentSettings();
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


    private static void LogConfigSectionIfChanged(string key, string message)
    {
        if (_lastConfigSectionSummaries.TryGetValue(key, out string? previous) &&
            string.Equals(previous, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastConfigSectionSummaries[key] = message;
        ModLog.Debug(message);
    }

    public static void RefreshFromNativeSettings()
    {
        ApplySettingsSnapshot(ChooseTheAncientSettingsStore.Load(), "native");
    }

    public static void ResetAllSettingsToDefaults()
    {
        ApplySettingsSnapshot(ChooseTheAncientSettingsStore.ResetToDefaults(), "reset-defaults");
        ModLog.Info("Reset all Choose The Ancient settings to built-in defaults.");
    }

    internal static void ApplySettingsSnapshot(ChooseTheAncientSettings settings, string source)
    {
        EnableRedundantSettings = settings.EnableRedundantSettings;
        AncientCount = NormalizeAncientCount(settings.AncientCount);
        ShowControllerHotkeys = settings.ShowControllerHotkeys;
        ShowOnlyButtonOutline = settings.ShowOnlyButtonOutline;
        VoteClickTarget = NormalizeVoteClickTarget(settings.VoteClickTarget);
        GameMode = NormalizeSelectionGameMode(settings.GameMode);

        for (int targetActIndex = 0; targetActIndex < AncientPoolSourceActCount; targetActIndex++)
        {
            if (!AncientPoolSourceActsByTargetAct.TryGetValue(targetActIndex, out bool[]? sourceActFlags))
                continue;

            for (int sourceActIndex = 0; sourceActIndex < AncientPoolSourceActCount; sourceActIndex++)
            {
                string key = GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex);
                sourceActFlags[sourceActIndex] = settings.AncientPoolSourceActs.TryGetValue(key, out bool enabled)
                    ? enabled
                    : GetDefaultAncientPoolSourceActEnabled(targetActIndex, sourceActIndex);
            }
        }

        foreach (string ancientId in new[] { NeowAncientId, DarvAncientId })
        {
            if (!SpecialAncientOverridesByTargetAct.TryGetValue(ancientId, out bool[]? actFlags))
                continue;

            for (int targetActIndex = 0; targetActIndex < actFlags.Length; targetActIndex++)
            {
                string key = GetSpecialAncientOverrideConfigKey(ancientId, targetActIndex);
                actFlags[targetActIndex] = settings.SpecialAncientOverrides.TryGetValue(key, out bool enabled)
                    ? enabled
                    : GetDefaultSpecialAncientOverrideEnabled(ancientId, targetActIndex);
            }
        }

        CurrentLogLevel = NormalizeLogLevel(settings.LogLevel);
        CurrentLogBackend = NormalizeLogBackend(settings.LogBackend);
        ModLog.Configure(CurrentLogLevel, CurrentLogBackend, source);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
        LogRefreshSummary();
    }

    private static void PersistCurrentSettings()
    {
        ChooseTheAncientSettingsStore.SaveCurrent();
    }

    private static void LogRefreshSummary()
    {
        string refreshSummary =
            $"EnableRedundantSettings={EnableRedundantSettings}, " +
            $"AncientCount={AncientCount}, GameMode={GameMode}, " +
            $"Act1Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(0))}, " +
            $"Act2Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(1))}, " +
            $"Act3Sources={DescribeAncientPoolSourceActs(GetEnabledAncientPoolSourceActs(2))}, " +
            $"NeowOverrides={DescribeSpecialAncientOverrides(NeowAncientId)}, " +
            $"DarvOverrides={DescribeSpecialAncientOverrides(DarvAncientId)}, " +
            $"LogLevel={CurrentLogLevel}, LogBackend={CurrentLogBackend}.";

        if (!string.Equals(_lastRefreshSummary, refreshSummary, StringComparison.Ordinal))
        {
            _lastRefreshSummary = refreshSummary;
            ModLog.Info("Config refresh complete. " + refreshSummary);
        }
    }

    public static void ApplyEnableRedundantSettings(object value)
    {
        EnableRedundantSettings = Convert.ToBoolean(value);
        ModLog.Info(
            $"Redundant legacy ancient settings are now {(EnableRedundantSettings ? "enabled" : "disabled")}. " +
            "Saved child choices are preserved.");
        PersistCurrentSettings();
    }

    public static void ApplyAncientCount(object value)
    {
        AncientCount = NormalizeAncientCount(value);
        PersistCurrentSettings();
    }

    public static void ApplyShowControllerHotkeys(object value)
    {
        ShowControllerHotkeys = Convert.ToBoolean(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
        PersistCurrentSettings();
    }

    public static void ApplyShowOnlyButtonOutlineHotkeys(object value)
    {
        ShowOnlyButtonOutline = Convert.ToBoolean(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
        PersistCurrentSettings();
    }

    public static void ApplyVoteClickTarget(object value)
    {
        VoteClickTarget = NormalizeVoteClickTarget(value);
        ChooseTheAncientSelectionScreen.RefreshModConfigHotkeys();
        PersistCurrentSettings();
    }

    public static void ApplySelectionGameMode(object value)
    {
        GameMode = NormalizeSelectionGameMode(value);
        PersistCurrentSettings();
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

        return string.Join(", ", enabledSourceActList.Select(sourceActIndex => GetAncientPoolSourceActLogLabel(sourceActIndex)));
    }

    public static void ApplyAncientPoolSourceActToggle(int targetActIndex, int sourceActIndex, object value)
    {
        if (!EnableRedundantSettings)
        {
            ModLog.Debug(
                $"Ignoring ancient-pool source-act change for target act {targetActIndex + 1} " +
                "because redundant settings are disabled.");
            return;
        }

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
        PersistCurrentSettings();
    }

    public static string GetAncientPoolSourceActConfigKey(int targetActIndex, int sourceActIndex)
    {
        return $"act{targetActIndex + 1}AncientsFromAct{sourceActIndex + 1}";
    }

    private static string GetAncientPoolSourceActLogLabel(int sourceActIndex)
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

    public static void ApplyLogBackend(object value)
    {
        CurrentLogBackend = NormalizeLogBackend(value);
        ModLog.Configure(CurrentLogLevel, CurrentLogBackend, "settings");
        PersistCurrentSettings();
    }

    public static void ApplyLogLevel(object value)
    {
        CurrentLogLevel = NormalizeLogLevel(value);
        ModLog.Configure(CurrentLogLevel, CurrentLogBackend, "settings");
        PersistCurrentSettings();
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

    public static string LogBackendToOption(LogBackend backend)
    {
        return backend switch
        {
            LogBackend.BaseGame => LogBackendOptions[0],
            LogBackend.ModLog => LogBackendOptions[1],
            _ => LogBackendOptions[0]
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
            LogLevel.VeryDebug => LogLevelOptions[4],
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

    internal static string[] GetLocalizedSelectionGameModeOptions() =>
        ResolveLocalizedOptions(SelectionGameModeOptionLocKeys);

    internal static string[] GetLocalizedVoteClickTargetOptions() =>
        ResolveLocalizedOptions(VoteClickTargetOptionLocKeys);

    internal static string[] GetLocalizedLogBackendOptions() =>
        ResolveLocalizedOptions(LogBackendOptionLocKeys);

    internal static string[] GetLocalizedLogLevelOptions() =>
        ResolveLocalizedOptions(LogLevelOptionLocKeys);

    internal static string GetLocalizedSelectionGameModeOption(SelectionGameMode mode) =>
        GetLocalizedOption(SelectionGameModeOptionLocKeys, (int)mode);

    internal static string GetLocalizedVoteClickTargetOption(VoteClickTargetMode mode) =>
        GetLocalizedOption(VoteClickTargetOptionLocKeys, (int)mode);

    internal static string GetLocalizedLogBackendOption(LogBackend backend) =>
        GetLocalizedOption(LogBackendOptionLocKeys, (int)backend);

    internal static string GetLocalizedLogLevelOption(LogLevel level) =>
        GetLocalizedOption(LogLevelOptionLocKeys, (int)level);

    private static string[] ResolveLocalizedOptions(IReadOnlyList<string> optionLocKeys)
    {
        string[] options = new string[optionLocKeys.Count];
        for (int i = 0; i < optionLocKeys.Count; i++)
            options[i] = ChooseTheAncientLocalization.GetSettingsText(optionLocKeys[i]);

        return options;
    }

    private static string GetLocalizedOption(IReadOnlyList<string> optionLocKeys, int index)
    {
        if (index < 0 || index >= optionLocKeys.Count)
            index = 0;

        return ChooseTheAncientLocalization.GetSettingsText(optionLocKeys[index]);
    }

    private static bool TryGetLocalizedOptionIndex(
        string value,
        IReadOnlyList<string> canonicalOptions,
        IReadOnlyList<string> optionLocKeys,
        out int index)
    {
        int count = Math.Min(canonicalOptions.Count, optionLocKeys.Count);
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(value, canonicalOptions[i], StringComparison.OrdinalIgnoreCase)
                || ChooseTheAncientLocalization.MatchesKnownTranslation(
                    value,
                    ChooseTheAncientLocalization.SettingsTableName,
                    optionLocKeys[i]))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    internal static SelectionGameMode NormalizeSelectionGameMode(object value)
    {
        if (value is SelectionGameMode mode)
            return mode;

        if (value is string rawString)
        {
            if (TryGetLocalizedOptionIndex(
                    rawString,
                    SelectionGameModeOptions,
                    SelectionGameModeOptionLocKeys,
                    out int localizedIndex))
            {
                return (SelectionGameMode)localizedIndex;
            }

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
            if (TryGetLocalizedOptionIndex(
                    rawString,
                    VoteClickTargetOptions,
                    VoteClickTargetOptionLocKeys,
                    out int localizedIndex))
            {
                return (VoteClickTargetMode)localizedIndex;
            }

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
                return NormalizeVoteClickTargetNumeric(parsedInt);
        }

        int rawValue = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultVoteClickTarget
        };

        return NormalizeVoteClickTargetNumeric(rawValue);
    }

    private static VoteClickTargetMode NormalizeVoteClickTargetNumeric(int rawValue)
    {
        rawValue = Math.Clamp(rawValue, (int)VoteClickTargetMode.ButtonOnly, (int)VoteClickTargetMode.WholeSlot);
        return (VoteClickTargetMode)rawValue;
    }

    private static LogBackend NormalizeLogBackend(object value)
    {
        if (value is LogBackend backend)
            return backend;

        if (value is string rawString)
        {
            if (TryGetLocalizedOptionIndex(
                    rawString,
                    LogBackendOptions,
                    LogBackendOptionLocKeys,
                    out int localizedIndex))
            {
                return (LogBackend)localizedIndex;
            }

            if (string.Equals(rawString, LogBackendOptions[0], StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, nameof(LogBackend.BaseGame), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, "Base game logger", StringComparison.OrdinalIgnoreCase))
            {
                return LogBackend.BaseGame;
            }

            if (string.Equals(rawString, LogBackendOptions[1], StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, nameof(LogBackend.ModLog), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, "Godot direct (legacy)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, "Godot direct", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawString, "GodotDirect", StringComparison.OrdinalIgnoreCase))
            {
                return LogBackend.ModLog;
            }

            if (int.TryParse(rawString, out int parsedInt))
                return NormalizeLogBackend(parsedInt);
        }

        int rawValue = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultLogBackend
        };

        rawValue = Math.Clamp(rawValue, (int)LogBackend.BaseGame, (int)LogBackend.ModLog);
        return (LogBackend)rawValue;
    }

    private static LogLevel NormalizeLogLevel(object value)
    {
        if (value is LogLevel level)
            return level;

        if (value is string rawString)
        {
            if (TryGetLocalizedOptionIndex(
                    rawString,
                    LogLevelOptions,
                    LogLevelOptionLocKeys,
                    out int localizedIndex))
            {
                return (LogLevel)localizedIndex;
            }

            // "Trace" was the previous display/saved value for VeryDebug.
            if (string.Equals(rawString, "Trace", StringComparison.OrdinalIgnoreCase))
                return LogLevel.VeryDebug;

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

        rawValue = Math.Clamp(rawValue, (int)LogLevel.Error, (int)LogLevel.VeryDebug);
        return (LogLevel)rawValue;
    }
}
