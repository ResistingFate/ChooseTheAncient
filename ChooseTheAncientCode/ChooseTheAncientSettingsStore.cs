using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal sealed class ChooseTheAncientSettings
{
    public int AncientCount { get; set; } = ChooseTheAncientConfig.DefaultAncientCount;

    public string GameMode { get; set; } =
        ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.DefaultSelectionGameMode);

    public bool ShowControllerHotkeys { get; set; } = ChooseTheAncientConfig.DefaultShowControllerHotkeys;
    public bool ShowOnlyButtonOutline { get; set; } = ChooseTheAncientConfig.DefaultShowOnlyButtonOutline;

    public string VoteClickTarget { get; set; } =
        ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.DefaultVoteClickTarget);

    public string LogBackend { get; set; } =
        ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.DefaultLogBackend);

    public string LogLevel { get; set; } =
        ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.DefaultLogLevel);

    public Dictionary<string, bool> AncientPoolSourceActs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> SpecialAncientOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal static class ChooseTheAncientSettingsStore
{
    private const string FileName = "ChooseTheAncient.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static ChooseTheAncientSettings? _loaded;
    private static bool _loadedFromDisk;

    internal static bool LoadedFromDisk
    {
        get
        {
            EnsureLoaded();
            return _loadedFromDisk;
        }
    }

    internal static string SettingsPath =>
        Path.Combine(OS.GetUserDataDir(), "mod_configs", FileName);

    internal static ChooseTheAncientSettings Load()
    {
        EnsureLoaded();
        return _loaded!;
    }

    internal static ChooseTheAncientSettings ReloadFromDisk()
    {
        _loaded = null;
        _loadedFromDisk = false;
        EnsureLoaded();
        return _loaded!;
    }

    internal static void SaveCurrent()
    {
        Save(CreateSnapshotFromRuntime());
    }

    internal static void Save(ChooseTheAncientSettings settings)
    {
        _loaded = settings;

        try
        {
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            _loadedFromDisk = true;
            ModLog.Debug($"Saved native settings to {path}.");
        }
        catch (Exception e)
        {
            ModLog.Warn($"Failed to save native settings '{SettingsPath}': {e.Message}");
        }
    }

    internal static ChooseTheAncientSettings CreateSnapshotFromRuntime()
    {
        var settings = new ChooseTheAncientSettings
        {
            AncientCount = ChooseTheAncientConfig.AncientCount,
            GameMode = ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode),
            ShowControllerHotkeys = ChooseTheAncientConfig.ShowControllerHotkeys,
            ShowOnlyButtonOutline = ChooseTheAncientConfig.ShowOnlyButtonOutline,
            VoteClickTarget = ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget),
            LogBackend = ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend),
            LogLevel = ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel),
            AncientPoolSourceActs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            SpecialAncientOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        };

        for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
        {
            for (int sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
            {
                string key = ChooseTheAncientConfig.GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex);
                bool enabled = ChooseTheAncientConfig
                    .GetEnabledAncientPoolSourceActs(targetActIndex)
                    .Contains(sourceActIndex);
                settings.AncientPoolSourceActs[key] = enabled;
            }
        }

        foreach (string ancientId in new[] { "NEOW", "DARV" })
        {
            for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
            {
                string key = ChooseTheAncientConfig.GetSpecialAncientOverrideConfigKey(ancientId, targetActIndex);
                settings.SpecialAncientOverrides[key] =
                    ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled(ancientId, targetActIndex);
            }
        }

        return settings;
    }

    private static void EnsureLoaded()
    {
        if (_loaded != null)
            return;

        string path = SettingsPath;

        if (!File.Exists(path))
        {
            _loaded = CreateDefaultSettings();
            _loadedFromDisk = false;
            ModLog.Info($"No native settings file found at {path}; using built-in defaults until settings are changed.");
            return;
        }

        try
        {
            string raw = File.ReadAllText(path);
            _loaded = JsonSerializer.Deserialize<ChooseTheAncientSettings>(raw, JsonOptions) ?? CreateDefaultSettings();
            NormalizeDictionaries(_loaded);
            FillMissingDefaults(_loaded);
            _loadedFromDisk = true;
            ModLog.Info($"Loaded native settings from {path}.");
        }
        catch (Exception e)
        {
            _loaded = CreateDefaultSettings();
            _loadedFromDisk = false;
            ModLog.Warn($"Failed to load native settings '{path}'; using defaults. Error: {e.Message}");
        }
    }

    private static ChooseTheAncientSettings CreateDefaultSettings()
    {
        var settings = new ChooseTheAncientSettings();

        for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
        {
            for (int sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
            {
                string key = ChooseTheAncientConfig.GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex);
                settings.AncientPoolSourceActs[key] =
                    ChooseTheAncientConfig.GetDefaultAncientPoolSourceActEnabled(targetActIndex, sourceActIndex);
            }
        }

        foreach (string ancientId in new[] { "NEOW", "DARV" })
        {
            for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
            {
                string key = ChooseTheAncientConfig.GetSpecialAncientOverrideConfigKey(ancientId, targetActIndex);
                settings.SpecialAncientOverrides[key] =
                    ChooseTheAncientConfig.GetDefaultSpecialAncientOverrideEnabled(ancientId, targetActIndex);
            }
        }

        return settings;
    }

    private static void NormalizeDictionaries(ChooseTheAncientSettings settings)
    {
        settings.AncientPoolSourceActs =
            settings.AncientPoolSourceActs == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(settings.AncientPoolSourceActs, StringComparer.OrdinalIgnoreCase);
        settings.SpecialAncientOverrides =
            settings.SpecialAncientOverrides == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(settings.SpecialAncientOverrides, StringComparer.OrdinalIgnoreCase);
    }

    private static void FillMissingDefaults(ChooseTheAncientSettings settings)
    {
        var defaults = CreateDefaultSettings();

        foreach (var (key, value) in defaults.AncientPoolSourceActs)
            settings.AncientPoolSourceActs.TryAdd(key, value);

        foreach (var (key, value) in defaults.SpecialAncientOverrides)
            settings.SpecialAncientOverrides.TryAdd(key, value);
    }
}
