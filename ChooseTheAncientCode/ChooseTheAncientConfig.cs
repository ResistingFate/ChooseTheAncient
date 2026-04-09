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

    private static readonly Dictionary<int, bool[]> AncientPoolSourceActsByTargetAct = new()
    {
        // Act 1 ancients are not supported yet.
        // Leave this commented row in place so it is easy to restore once the
        // mod starts intercepting the Act 1 ancient selection flow.
        // { 0, new[] { true, true, true } },

        // Act 2 ancients.
        { 1, new[] { true, true, true } },

        // Act 3 ancients.
        { 2, new[] { true, true, true } },
    };

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
            return;

        if (sourceActIndex < 0 || sourceActIndex >= sourceActFlags.Length)
            return;

        sourceActFlags[sourceActIndex] = Convert.ToBoolean(value);
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
            for (int sourceActIndex = 0; sourceActIndex < sourceActFlags.Length; sourceActIndex++)
            {
                string key = GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex);
                sourceActFlags[sourceActIndex] = ModConfigBridge.GetValue(key, true);
            }
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
