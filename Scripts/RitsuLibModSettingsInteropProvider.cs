using System.Globalization;
using ChooseTheAncient.ChooseTheAncientCode;

namespace ChooseTheAncient.Scripts;

internal static class RitsuLibModSettingsInteropProvider
{
    public static object CreateRitsuLibSettingsSchema()
    {
        return "res://ChooseTheAncient/interop/ritsulib_settings_schema.json";
    }

    public static object? GetRitsuLibSettingValue(string key)
    {
        return key switch
        {
            "ancientCount" => ChooseTheAncientConfig.AncientCount,
            "showControllerHotkeys" => ChooseTheAncientConfig.ShowControllerHotkeys,
            "showOnlyButtonOutline" => ChooseTheAncientConfig.ShowOnlyButtonOutline,
            "voteClickTarget" => ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget),
            "logLevel" => ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel),
            _ => null,
        };
    }

    public static void SetRitsuLibSettingValue(string key, object? value)
    {
        switch (key)
        {
            case "ancientCount":
            {
                var count = ToInt(value, ChooseTheAncientConfig.AncientCount);
                ChooseTheAncientConfig.ApplyAncientCount(count);
                ModConfigBridge.SetValue("ancientCount", (float)ChooseTheAncientConfig.AncientCount);
                return;
            }
            case "showControllerHotkeys":
            {
                var enabled = ToBool(value, ChooseTheAncientConfig.ShowControllerHotkeys);
                ChooseTheAncientConfig.ApplyShowControllerHotkeys(enabled);
                ModConfigBridge.SetValue("showControllerHotkeys", enabled);
                return;
            }
            case "showOnlyButtonOutline":
            {
                var enabled = ToBool(value, ChooseTheAncientConfig.ShowOnlyButtonOutline);
                ChooseTheAncientConfig.ApplyShowOnlyButtonOutlineHotkeys(enabled);
                ModConfigBridge.SetValue("showOnlyButtonOutline", enabled);
                return;
            }
            case "voteClickTarget":
            {
                var option = value?.ToString() ?? ChooseTheAncientConfig.VoteClickTargetToOption(
                    ChooseTheAncientConfig.VoteClickTarget);
                ChooseTheAncientConfig.ApplyVoteClickTarget(option);
                var normalizedOption = ChooseTheAncientConfig.VoteClickTargetToOption(
                    ChooseTheAncientConfig.VoteClickTarget);
                ModConfigBridge.SetValue("voteClickTarget", normalizedOption);
                return;
            }
            case "logLevel":
            {
                var option = value?.ToString() ?? ChooseTheAncientConfig.LogLevelToOption(
                    ChooseTheAncientConfig.CurrentLogLevel);
                ChooseTheAncientConfig.ApplyLogLevel(option);
                var normalizedOption = ChooseTheAncientConfig.LogLevelToOption(
                    ChooseTheAncientConfig.CurrentLogLevel);
                ModConfigBridge.SetValue("logLevel", normalizedOption);
                return;
            }
        }
    }

    public static bool GetRitsuLibSettingBool(string key)
    {
        return key switch
        {
            "showControllerHotkeys" => ChooseTheAncientConfig.ShowControllerHotkeys,
            "showOnlyButtonOutline" => ChooseTheAncientConfig.ShowOnlyButtonOutline,
            _ => false,
        };
    }

    public static void SetRitsuLibSettingBool(string key, bool value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static int GetRitsuLibSettingInt(string key)
    {
        return key switch
        {
            "ancientCount" => ChooseTheAncientConfig.AncientCount,
            _ => 0,
        };
    }

    public static void SetRitsuLibSettingInt(string key, int value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static string GetRitsuLibSettingString(string key)
    {
        return key switch
        {
            "voteClickTarget" => ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget),
            "logLevel" => ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel),
            _ => "",
        };
    }

    public static void SetRitsuLibSettingString(string key, string value)
    {
        SetRitsuLibSettingValue(key, value);
    }

    public static void SaveRitsuLibSettings()
    {
    }

    public static void InvokeRitsuLibSettingAction(string key)
    {
        if (!string.Equals(key, "reloadConfig", StringComparison.Ordinal))
            return;

        ChooseTheAncientConfig.RefreshFromModConfig();
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
