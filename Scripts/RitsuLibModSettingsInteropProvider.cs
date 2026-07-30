using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode;

namespace ChooseTheAncient.Scripts;

internal static class RitsuLibModSettingsInteropProvider
{
    private static readonly string[] GameModeOptionIds =
        ["monty_hall", "fair_fight", "want_everything", "simple_picker"];

    private static readonly string[] VoteClickTargetOptionIds =
        ["button_only", "whole_card", "whole_ancient_slot"];

    private static readonly string[] LogBackendOptionIds =
        ["game_logging", "modlog"];

    private static readonly string[] LogLevelOptionIds =
        ["error", "warn", "info", "debug", "very_debug"];

    public static object CreateRitsuLibSettingsSchema()
    {
        return "res://ChooseTheAncient/settings/ritsulib_settings_schema.json";
    }

    public static object? GetRitsuLibSettingValue(string key)
    {
        if (TryParseAncientPoolSourceActKey(key, out int targetActIndex, out int sourceActIndex))
        {
            return ChooseTheAncientConfig
                .GetEnabledAncientPoolSourceActs(targetActIndex)
                .Contains(sourceActIndex);
        }

        if (TryParseSpecialAncientOverrideKey(key, out string? ancientId, out int specialTargetActIndex))
            return ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled(ancientId, specialTargetActIndex);

        return key switch
        {
            "enableRedundantSettings" => ChooseTheAncientConfig.EnableRedundantSettings,
            "ancientCount" => ChooseTheAncientConfig.AncientCount,
            "gameMode" => GetOptionId(GameModeOptionIds, (int)ChooseTheAncientConfig.GameMode),
            "showControllerHotkeys" => ChooseTheAncientConfig.ShowControllerHotkeys,
            "showOnlyButtonOutline" => ChooseTheAncientConfig.ShowOnlyButtonOutline,
            "voteClickTarget" => GetOptionId(VoteClickTargetOptionIds, (int)ChooseTheAncientConfig.VoteClickTarget),
            "logBackend" => GetOptionId(LogBackendOptionIds, (int)ChooseTheAncientConfig.CurrentLogBackend),
            "logLevel" => GetOptionId(LogLevelOptionIds, (int)ChooseTheAncientConfig.CurrentLogLevel),
            _ => null,
        };
    }

    public static void SetRitsuLibSettingValue(string key, object? value)
    {
        if (string.Equals(key, "enableRedundantSettings", StringComparison.Ordinal))
        {
            ChooseTheAncientConfig.ApplyEnableRedundantSettings(
                ToBool(value, ChooseTheAncientConfig.EnableRedundantSettings));
            return;
        }

        if (TryParseAncientPoolSourceActKey(key, out int targetActIndex, out int sourceActIndex))
        {
            ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(
                targetActIndex,
                sourceActIndex,
                ToBool(value, ChooseTheAncientConfig.GetDefaultAncientPoolSourceActEnabled(targetActIndex, sourceActIndex)));
            return;
        }

        if (TryParseSpecialAncientOverrideKey(key, out string? ancientId, out int specialTargetActIndex))
        {
            ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle(
                ancientId,
                specialTargetActIndex,
                ToBool(value, ChooseTheAncientConfig.GetDefaultSpecialAncientOverrideEnabled(ancientId, specialTargetActIndex)));
            return;
        }

        switch (key)
        {
            case "ancientCount":
            {
                var count = ToInt(value, ChooseTheAncientConfig.AncientCount);
                ChooseTheAncientConfig.ApplyAncientCount(count);
                ModConfigBridge.SetValue("ancientCount", (float)ChooseTheAncientConfig.AncientCount);
                return;
            }
            case "gameMode":
            {
                string option = ToCanonicalOption(
                    value,
                    GameModeOptionIds,
                    ChooseTheAncientConfig.SelectionGameModeOptions,
                    (int)ChooseTheAncientConfig.GameMode);
                ChooseTheAncientConfig.ApplySelectionGameMode(option);
                ModConfigBridge.SetValue(
                    "gameMode",
                    ChooseTheAncientConfig.GetLocalizedSelectionGameModeOption(ChooseTheAncientConfig.GameMode));
                return;
            }
            case "showControllerHotkeys":
            {
                var enabled = ToBool(value, ChooseTheAncientConfig.ShowControllerHotkeys);
                ChooseTheAncientConfig.ApplyShowControllerHotkeys(enabled);
                return;
            }
            case "showOnlyButtonOutline":
            {
                var enabled = ToBool(value, ChooseTheAncientConfig.ShowOnlyButtonOutline);
                ChooseTheAncientConfig.ApplyShowOnlyButtonOutlineHotkeys(enabled);
                return;
            }
            case "voteClickTarget":
            {
                string option = ToCanonicalOption(
                    value,
                    VoteClickTargetOptionIds,
                    ChooseTheAncientConfig.VoteClickTargetOptions,
                    (int)ChooseTheAncientConfig.VoteClickTarget);
                ChooseTheAncientConfig.ApplyVoteClickTarget(option);
                return;
            }
            case "logBackend":
            {
                string option = ToCanonicalOption(
                    value,
                    LogBackendOptionIds,
                    ChooseTheAncientConfig.LogBackendOptions,
                    (int)ChooseTheAncientConfig.CurrentLogBackend);
                ChooseTheAncientConfig.ApplyLogBackend(option);
                ModConfigBridge.SetValue(
                    "logBackend",
                    ChooseTheAncientConfig.GetLocalizedLogBackendOption(ChooseTheAncientConfig.CurrentLogBackend));
                return;
            }
            case "logLevel":
            {
                string option = ToCanonicalOption(
                    value,
                    LogLevelOptionIds,
                    ChooseTheAncientConfig.LogLevelOptions,
                    (int)ChooseTheAncientConfig.CurrentLogLevel);
                ChooseTheAncientConfig.ApplyLogLevel(option);
                ModConfigBridge.SetValue(
                    "logLevel",
                    ChooseTheAncientConfig.GetLocalizedLogLevelOption(ChooseTheAncientConfig.CurrentLogLevel));
                return;
            }
        }
    }
    
    private static string GetOptionId(IReadOnlyList<string> optionIds, int index)
    {
        /*
         * If Gome Mod is being selected, it's selected via index from optionIds
         * for option 1, it will return fair_fight.
         */
        if (optionIds.Count == 0)
            return "";

        int safeIndex = Math.Clamp(index, 0, optionIds.Count - 1);
        return optionIds[safeIndex];
    }

    private static string ToCanonicalOption(
        object? value,
        IReadOnlyList<string> optionIds,
        IReadOnlyList<string> canonicalOptions,
        int fallbackIndex)
    {
        /*
         * If Game Mode is being selected and the first optionIds is fair_fight
         * this returns it's actual value for eng which is Fair Fight.
         */
        int optionCount = Math.Min(optionIds.Count, canonicalOptions.Count);
        string text = value?.ToString() ?? "";

        for (int index = 0; index < optionCount; index++)
        {
            if (string.Equals(text, optionIds[index], StringComparison.OrdinalIgnoreCase))
                return canonicalOptions[index];
        }

        if (!string.IsNullOrWhiteSpace(text))
            return text;

        if (canonicalOptions.Count == 0)
            return "";

        int safeIndex = Math.Clamp(fallbackIndex, 0, canonicalOptions.Count - 1);
        return canonicalOptions[safeIndex];
    }

    public static bool IsRitsuLibRedundantSettingsEnabled()
    {
        return ChooseTheAncientConfig.EnableRedundantSettings;
    }

    public static bool GetRitsuLibSettingBool(string key)
    {
        return GetRitsuLibSettingValue(key) is bool value && value;
    }

    public static void SetRitsuLibSettingBool(string key, bool value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static int GetRitsuLibSettingInt(string key)
    {
        return GetRitsuLibSettingValue(key) is int value ? value : 0;
    }

    public static void SetRitsuLibSettingInt(string key, int value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static string GetRitsuLibSettingString(string key)
    {
        return GetRitsuLibSettingValue(key)?.ToString() ?? "";
    }

    public static void SetRitsuLibSettingString(string key, string value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static void SaveRitsuLibSettings()
    {
        ChooseTheAncientSettingsStore.SaveCurrent();
        ModConfigBridge.PushImportantSettingsToModConfig();
    }

    public static void InvokeRitsuLibSettingAction(string key)
    {
        if (!string.Equals(key, "resetConfig", StringComparison.Ordinal))
            return;

        ChooseTheAncientConfig.ResetAllSettingsToDefaults();
        ModConfigBridge.PushImportantSettingsToModConfig();
    }

    private static bool TryParseAncientPoolSourceActKey(string key, out int targetActIndex, out int sourceActIndex)
    {
        for (targetActIndex = 0; targetActIndex < 3; targetActIndex++)
        {
            for (sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
            {
                if (string.Equals(
                        key,
                        ChooseTheAncientConfig.GetAncientPoolSourceActConfigKey(targetActIndex, sourceActIndex),
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        targetActIndex = -1;
        sourceActIndex = -1;
        return false;
    }

    private static bool TryParseSpecialAncientOverrideKey(string key, out string ancientId, out int targetActIndex)
    {
        foreach (string id in new[] { "NEOW", "DARV" })
        {
            for (targetActIndex = 0; targetActIndex < 3; targetActIndex++)
            {
                if (string.Equals(
                        key,
                        ChooseTheAncientConfig.GetSpecialAncientOverrideConfigKey(id, targetActIndex),
                        StringComparison.Ordinal))
                {
                    ancientId = id;
                    return true;
                }
            }
        }

        ancientId = "";
        targetActIndex = -1;
        return false;
    }

    private static bool ToBool(object? value, bool fallback)
    {
        if (value == null)
            return fallback;
        if (value is bool b)
            return b;
        if (value is string s && bool.TryParse(s, out var sb))
            return sb;
        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static int ToInt(object? value, int fallback)
    {
        if (value == null)
            return fallback;
        if (value is int i)
            return i;
        if (value is long l)
            return (int)l;
        if (value is float f)
            return (int)Math.Round(f);
        if (value is double d)
            return (int)Math.Round(d);
        if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si))
            return si;
        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }
}
