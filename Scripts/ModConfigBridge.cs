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

    // ─── Step 1: Call this in your Initialize() ─────────────────
    // ModConfig may load AFTER your mod (alphabetical order).
    // Deferring to the next frame ensures ModConfig is ready.

    private static int _deferredFramesRemaining;

    internal static void DeferredRegister()
    {
        _deferredFramesRemaining = 2;
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame -= OnNextFrame;
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();

        if (_deferredFramesRemaining > 0)
        {
            _deferredFramesRemaining--;
            return;
        }

        tree.ProcessFrame -= OnNextFrame;
        Detect();
        if (_available)
        {
            Register();
            ChooseTheAncientConfig.RefreshFromModConfig();
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
        }
        catch
        {
            _available = false;
        }
    }

    // ─── Step 3: Register your config entries ───────────────────

    private static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            var entries = BuildEntries();

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
        if (!_available) return fallback;
        try
        {
            var result = _apiType!.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(typeof(T))
                ?.Invoke(null, new object[] { "ChooseTheAncient", key });
            return result != null ? (T)result : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>
    /// Sync a value back to ModConfig (for persistence).
    /// Call this when your mod changes a setting outside ModConfig's UI
    /// (e.g. via hotkey or your own settings menu).
    /// </summary>
    internal static void SetValue(string key, object value)
    {
        if (!_available) return;
        try
        {
            _apiType!.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { "ChooseTheAncient", key, value });
        }
        catch { }
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

            // Slider example in ModConfig uses float, so this is the safest shape.
            Set(cfg, "DefaultValue", (object)(float)ChooseTheAncientConfig.DefaultAncientCount);
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
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.DefaultSelectionGameMode));
            Set(cfg, "Options", ChooseTheAncientConfig.SelectionGameModeOptions);

            Set(cfg, "Description", "" +
                                    "\n     Monty Hall: 2 rounds, only the reaction ancient previews in round: 2." +
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
            Set(cfg, "Type", EnumVal("Separator"));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Label", "Ancient Pool Sources");
            Set(cfg, "Type", EnumVal("Header"));
        }));

        // Act 1 ancients are not supported yet.
        // Leave this line commented so it is easy to restore later.
        // AddAncientPoolSourceEntryGroup(list, targetActIndex: 0);

        AddAncientPoolSourceEntryGroup(list, targetActIndex: 1);
        AddAncientPoolSourceEntryGroup(list, targetActIndex: 2);

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "showControllerHotkeys");
            Set(cfg, "Label", "Show controller hotkeys");
            Set(cfg, "Type", EnumVal("Toggle"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.DefaultShowControllerHotkeys);

            Set(cfg, "Description", "Show controller/keyboard prompt hints on the ancient selection screen.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyShowControllerHotkeys(v);
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "showOnlyButtonOutline");
            Set(cfg, "Label", "Alternative Vote Button Design");
            Set(cfg, "Type", EnumVal("Toggle"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.DefaultShowOnlyButtonOutline);

            Set(cfg, "Description", "Only shows the White Outline and Text for the Vote Buttons.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyShowOnlyButtonOutlineHotkeys(v);
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "voteClickTarget");
            Set(cfg, "Label", "Vote click area");
            Set(cfg, "Type", EnumVal("Dropdown"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.DefaultVoteClickTarget));
            Set(cfg, "Options", ChooseTheAncientConfig.VoteClickTargetOptions);

            Set(cfg, "Description", "Choose whether only the button, the whole card, or the whole ancient slot can be clicked.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyVoteClickTarget(v);
            }));
        }));

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "logLevel");
            Set(cfg, "Label", "Log level");
            Set(cfg, "Type", EnumVal("Dropdown"));
            Set(cfg, "DefaultValue", (object)ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.DefaultLogLevel));
            Set(cfg, "Options", ChooseTheAncientConfig.LogLevelOptions);

            Set(cfg, "Description", "Controls how much ChooseTheAncient writes to the log. Changes apply immediately.");

            Set(cfg, "OnChanged", new Action<object>(v =>
            {
                ChooseTheAncientConfig.ApplyLogLevel(v);
            }));
        }));
        
        var result = Array.CreateInstance(_entryType!, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            result.SetValue(list[i], i);
        }

        return result;
    } 

    private static void AddAncientPoolSourceEntryGroup(List<object> list, int targetActIndex)
    {
        list.Add(Entry(cfg =>
        {
            Set(cfg, "Label", ChooseTheAncientConfig.GetAncientPoolTargetActLabel(targetActIndex));
            Set(cfg, "Type", EnumVal("Header"));
        }));

        for (int sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
        {
            int capturedTargetActIndex = targetActIndex;
            int capturedSourceActIndex = sourceActIndex;

            list.Add(Entry(cfg =>
            {
                Set(cfg, "Key", ChooseTheAncientConfig.GetAncientPoolSourceActConfigKey(capturedTargetActIndex, capturedSourceActIndex));
                Set(cfg, "Label", ChooseTheAncientConfig.GetAncientPoolSourceActLabel(capturedSourceActIndex));
                Set(cfg, "Type", EnumVal("Toggle"));
                Set(cfg, "DefaultValue", (object)true);
                Set(cfg, "Description",
                    $"Allow ancients that normally come from Act {capturedSourceActIndex + 1} to appear in the Act {capturedTargetActIndex + 1} Choose The Ancient pool.");

                Set(cfg, "OnChanged", new Action<object>(v =>
                {
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(capturedTargetActIndex, capturedSourceActIndex, v);
                    ModLog.Info(
                        $"{ChooseTheAncientConfig.GetAncientPoolTargetActLabel(capturedTargetActIndex)} / " +
                        $"{ChooseTheAncientConfig.GetAncientPoolSourceActLabel(capturedSourceActIndex)} changed to {v}");
                }));
            }));
        }

        list.Add(Entry(cfg =>
        {
            Set(cfg, "Type", EnumVal("Separator"));
        }));
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
