using System;
using System.Globalization;
using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode;

namespace ChooseTheAncient.Scripts;

internal static class RitsuLibModSettingsInteropProvider
{
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
            "ancientCount" => ChooseTheAncientConfig.AncientCount,
            "gameMode" => ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode),
            "showControllerHotkeys" => ChooseTheAncientConfig.ShowControllerHotkeys,
            "showOnlyButtonOutline" => ChooseTheAncientConfig.ShowOnlyButtonOutline,
            "voteClickTarget" => ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget),
            "logBackend" => ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend),
            "logLevel" => ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel),
            _ => null,
        };
    }

    public static void SetRitsuLibSettingValue(string key, object? value)
    {
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
                var option = value?.ToString() ?? ChooseTheAncientConfig.SelectionGameModeToOption(
                    ChooseTheAncientConfig.GameMode);
                ChooseTheAncientConfig.ApplySelectionGameMode(option);
                ModConfigBridge.SetValue(
                    "gameMode",
                    ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode));
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
                var option = value?.ToString() ?? ChooseTheAncientConfig.VoteClickTargetToOption(
                    ChooseTheAncientConfig.VoteClickTarget);
                ChooseTheAncientConfig.ApplyVoteClickTarget(option);
                return;
            }
            case "logBackend":
            {
                var option = value?.ToString() ?? ChooseTheAncientConfig.LogBackendToOption(
                    ChooseTheAncientConfig.CurrentLogBackend);
                ChooseTheAncientConfig.ApplyLogBackend(option);
                ModConfigBridge.SetValue(
                    "logBackend",
                    ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend));
                return;
            }
            case "logLevel":
            {
                var option = value?.ToString() ?? ChooseTheAncientConfig.LogLevelToOption(
                    ChooseTheAncientConfig.CurrentLogLevel);
                ChooseTheAncientConfig.ApplyLogLevel(option);
                ModConfigBridge.SetValue(
                    "logLevel",
                    ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel));
                return;
            }
        }
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
