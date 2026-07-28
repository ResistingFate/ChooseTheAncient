// =============================================================================
// ModConfigBridge.cs — Drop-in Template for ModConfig Integration
// =============================================================================
// Copy this file into your mod's Scripts/ folder, then:
//   1. Replace "YourMod" namespace and mod IDs with your own
//   2. Edit BuildEntries() to define your config items
//   3. Call ModConfigBridge.DeferredRegister() in your mod's Initialize()
//
// Zero DLL reference needed — everything is done via reflection.
// If ModConfig is not installed, your mod works normally (all GetValue calls
// return the fallback you provide).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChooseTheAncient.ChooseTheAncientCode;
using Godot;

namespace ChooseTheAncient.Scripts;

internal static class ModConfigBridge
{
    // ─── State ──────────────────────────────────────────────────
    private static bool _available;
    private static bool _registered;
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configTypeEnum;

    internal static bool IsAvailable => _available;
    private static readonly Dictionary<string, string> _lastReadValues =
        new(StringComparer.Ordinal);

    // ─── Step 1: Call this in your Initialize() ─────────────────
    // ModConfig may load AFTER your mod (alphabetical order).
    // Deferring to the next frame ensures ModConfig is ready.

    private static int _deferredFramesRemaining;
    private static bool _waitingForDeferredRegister;

    internal static void DeferredRegister()
    {
        if (_registered || _waitingForDeferredRegister)
        {
            return;
        }

        _waitingForDeferredRegister = true;
        _deferredFramesRemaining = 2;
        var tree = (SceneTree)Engine.GetMainLoop();
        ModLog.Debug("Scheduling deferred ModConfig registration for ChooseTheAncient.");
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();

        if (_deferredFramesRemaining > 0)
        {
            ModLog.Debug($"Waiting to register ModConfig. Frames remaining: {_deferredFramesRemaining}");
            _deferredFramesRemaining--;
            return;
        }

        tree.ProcessFrame -= OnNextFrame;
        _waitingForDeferredRegister = false;

        Detect();
        if (_available)
        {
            Register();

            if (!ChooseTheAncientSettingsStore.LoadedFromDisk)
                ChooseTheAncientConfig.ResetAllSettingsToDefaults();

            PushImportantSettingsToModConfig();
        }
        else
        {
            ModLog.Warn("ModConfig was not detected after deferred registration; using built-in defaults.");
        }
    }

    // ─── Step 2: Detect ModConfig via reflection ────────────────

    private static void Detect()
    {
        try
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .ToArray();

            _apiType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ModConfigApi");
            _entryType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigEntry");
            _configTypeEnum = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigType");
            _available = _apiType != null && _entryType != null && _configTypeEnum != null;

            ModLog.Debug(
                $"ModConfig detect complete. Available={_available}, " +
                $"ApiType={_apiType?.FullName ?? "<null>"}, " +
                $"EntryType={_entryType?.FullName ?? "<null>"}, " +
                $"ConfigTypeEnum={_configTypeEnum?.FullName ?? "<null>"}");
        }
        catch (Exception e)
        {
            _available = false;
            ModLog.Error($"ModConfig detection failed: {e}");
        }
    }

    // ─── Step 3: Register your config entries ───────────────────

    private static void Register()
    {
        if (_registered)
        {
            ModLog.Debug("Skipping ModConfig registration because entries are already registered.");
            return;
        }
        _registered = true;

        try
        {
            var entries = BuildEntries();
            ModLog.Debug($"Registering ChooseTheAncient ModConfig entries. Count={entries.Length}");

            // Localized display name (shows in ModConfig's mod list)
            var displayNames = new Dictionary<string, string>
            {
                ["en"] = "ChooseTheAncient",
                ["zhs"] = "你的模组名字", // TODO translate mod name
            };

            // ModConfig has 2 overloads: 3-param (no i18n) and 4-param (with i18n).
            // We prefer 4-param when available.
            var registerMethod = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length)
                .First();

            if (registerMethod.GetParameters().Length == 4)
            {
                registerMethod.Invoke(null, new object[]
                {
                    "ChooseTheAncient",          // Must match your mod's ID
                    displayNames["en"],     // Fallback display name
                    displayNames,           // Localized display names
                    entries
                });
            }
            else
            {
                registerMethod.Invoke(null, new object[]
                {
                    "ChooseTheAncient",
                    displayNames["en"],
                    entries
                });
            }
        }
        catch (Exception e)
        {
            // Log but don't crash — ModConfig is optional
            ModLog.Error($"ModConfig registration failed: {e}");
        }
    }

    // ─── Read/Write Config Values ───────────────────────────────

    /// <summary>Read a saved config value, with fallback if ModConfig absent.</summary>
    internal static T GetValue<T>(string key, T fallback)
    {
        if (!_available)
        {
            ModLog.Debug($"ModConfig GetValue<{typeof(T).Name}>('{key}') unavailable; using fallback '{fallback}'.");
            return fallback;
        }

        try
        {
            var result = _apiType!.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(typeof(T))
                ?.Invoke(null, new object[] { "ChooseTheAncient", key });

            T value = result != null ? (T)result : fallback;
            string valueText = Convert.ToString(value) ?? "<null>";

            if (!_lastReadValues.TryGetValue(key, out string? previous) ||
                !string.Equals(previous, valueText, StringComparison.Ordinal))
            {
                ModLog.Debug($"Loaded ModConfig key '{key}' = {valueText}.");
                _lastReadValues[key] = valueText;
            }

            return value;
        }
        catch (Exception e)
        {
            ModLog.Warn($"Failed to load ModConfig key '{key}'; using fallback '{fallback}'. Error: {e.Message}");
            return fallback;
        }
    }

    /// <summary>
    /// Sync a value back to ModConfig (for persistence).
    /// Call this when your mod changes a setting outside ModConfig's UI
    /// (e.g. via hotkey or your own settings menu).
    /// </summary>
    internal static void SetValue(string key, object value)
    {
        if (!_available)
        {
            ModLog.Debug($"Skipping ModConfig SetValue('{key}', '{value}') because ModConfig is unavailable.");
            return;
        }

        try
        {
            _apiType!.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { "ChooseTheAncient", key, value });
            ModLog.Debug($"Wrote ModConfig key '{key}' = {value}.");
        }
        catch (Exception e)
        {
            ModLog.Warn($"Failed to write ModConfig key '{key}' = {value}. Error: {e.Message}");
        }
    }

    internal static void PushImportantSettingsToModConfig()
    {
        if (!_available)
            return;

        SetValue("ancientCount", (float)ChooseTheAncientConfig.AncientCount);
        SetValue("gameMode", ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode));
        SetValue("logBackend", ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend));
        SetValue("logLevel", ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel));
    }


    // ═════════════════════════════════════════════════════════════
    //  EDIT BELOW: Define your config entries
    // ═════════════════════════════════════════════════════════════

    private static Array BuildEntries()
    {
        var list = new List<object>();

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Label", "Choose The Ancient");
            Set(cfg, "Type", EnumVal("Header"));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "ancientCount");
            Set(cfg, "Label", "Ancients in vote");
            Set(cfg, "Type", EnumVal("Slider"));
            Set(cfg, "DefaultValue", (object)(float)ChooseTheAncientConfig.AncientCount);
            Set(cfg, "Min", 2.0f);
            Set(cfg, "Max", 8.0f);
            Set(cfg, "Step", 1.0f);
            Set(cfg, "Format", "F0");
            Set(cfg, "Description", "How many ancients appear in the initial vote.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyAncientCount(v);
                ModLog.Info($"ancientCount changed to {v}");
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "gameMode");
            Set(cfg, "Label", "Game mode");
            Set(cfg, "Type", EnumVal("Dropdown"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode));
            Set(cfg, "Options", ChooseTheAncientConfig.SelectionGameModeOptions);

            Set(cfg, "Description", "" +
                                    "\n     Monty Hall: 2 rounds, only the reaction ancient previews in round 2." +
                                    "\n     Fair Fight: 2 rounds, both finalists preview in round 2." +
                                    "\n     I Want To Know Everything: 1 round, previews for every ancient, no dialogue." +
                                    "\n     Simple Picker: 1 round, no previews.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplySelectionGameMode(v);
                ModLog.Info($"gameMode changed to {ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode)}");
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Label", "Logging");
            Set(cfg, "Type", EnumVal("Header"));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "logBackend");
            Set(cfg, "Label", "Logging mode");
            Set(cfg, "Type", EnumVal("Dropdown"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.LogBackendToOption(
                ChooseTheAncientConfig.CurrentLogBackend));
            Set(cfg, "Options", ChooseTheAncientConfig.LogBackendOptions);
            Set(cfg, "Description",
                "Base game logger means this mods logging is handled by the base game's logging system. " +
                "Modlog lets this mod handle it's logging on it's own log level system.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyLogBackend(v);
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "logLevel");
            Set(cfg, "Label", "ModLog level");
            Set(cfg, "Type", EnumVal("Dropdown"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.LogLevelToOption(
                ChooseTheAncientConfig.CurrentLogLevel));
            Set(cfg, "Options", ChooseTheAncientConfig.LogLevelOptions);
            Set(cfg, "Description",
                "Only used when Logging mode is ModLog.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyLogLevel(v);
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "resetConfig");
            Set(cfg, "Label", "Reset all settings");
            Set(cfg, "Type", EnumVal("Button"));
            Set(cfg, "ButtonText", "Reset");
            Set(cfg, "Description", "Restore every Choose The Ancient setting to its built-in default.");

            Set(cfg, "OnChanged", new Action<object>(_ =>
            {
                ChooseTheAncientConfig.ResetAllSettingsToDefaults();
                PushImportantSettingsToModConfig();
            }));
        }));

        var result = Array.CreateInstance(_entryType!, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            result.SetValue(list[i], i);
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════
    //  Reflection helpers (don't need to modify these)
    // ═════════════════════════════════════════════════════════════

    private static object Entry(Action<object> configure)
    {
        var inst = Activator.CreateInstance(_entryType!)!;
        configure(inst);
        return inst;
    }

    private static void Set(object obj, string name, object value)
        => obj.GetType().GetProperty(name)?.SetValue(obj, value);

    private static Dictionary<string, string> L(string en, string zhs)
        => new() { ["en"] = en, ["zhs"] = zhs };

    private static object EnumVal(string name)
        => Enum.Parse(_configTypeEnum!, name);
}
