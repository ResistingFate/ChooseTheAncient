using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace ChooseTheAncient.ChooseTheAncientCode;

/// <summary>
/// Resolves localization of different languages for the mod settings and uses the eng table
/// as a fallback when the language does not contain the requested entry.
/// </summary>
internal static class ChooseTheAncientLocalization
{
    internal const string SettingsTableName = "settings_ui";
    internal const string GameplayUiTableName = "gameplay_ui";
    internal const string AncientsTableName = "ancients";

    private const string LocalizationRoot = "res://ChooseTheAncient/localization";
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> TableCache =
        new(StringComparer.Ordinal);

    internal static string GetText(
        string tableName,
        string key,
        params (string Name, object Value)[] variables)
    {
        if (TryGetActiveLocText(tableName, key, variables, out string? localized) &&
            localized is not null)
        {
            return localized;
        }

        string? englishTemplate = GetRawText("eng", tableName, key);
        return englishTemplate is null
            ? key
            : FormatTemplate(englishTemplate, variables);
    }

    internal static string GetSettingsText(
        string key,
        params (string Name, object Value)[] variables)
    {
        return GetText(SettingsTableName, key, variables);
    }

    internal static string GetSettingsTextForLanguage(
        string language,
        string key,
        params (string Name, object Value)[] variables)
    {
        return GetTextForLanguage(language, SettingsTableName, key, variables);
    }

    internal static Dictionary<string, string> GetSettingsModConfigLanguageMap(string key)
    {
        return GetModConfigLanguageMap(SettingsTableName, key);
    }

    internal static string GetTextForLanguage(
        string language,
        string tableName,
        string key,
        params (string Name, object Value)[] variables)
    {
        string? template = GetRawText(language, tableName, key)
                           ?? GetRawText("eng", tableName, key);

        return template == null
            ? key
            : FormatTemplate(template, variables);
    }

    internal static bool HasTextForLanguage(
        string language,
        string tableName,
        string key)
    {
        return GetTable(language, tableName).ContainsKey(key);
    }

    internal static bool HasActiveOrEnglishText(string tableName, string key)
    {
        try
        {
            if (LocManager.Instance.GetTable(tableName).HasEntry(key))
                return true;
        }
        catch
        {
            // Fall through to the explicit English table.
        }

        return HasTextForLanguage("eng", tableName, key);
    }

    internal static Dictionary<string, string> GetModConfigLanguageMap(
        string tableName,
        string key)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = GetTextForLanguage("eng", tableName, key),
            ["zhs"] = GetTextForLanguage("zhs", tableName, key)
        };
    }

    internal static bool MatchesKnownTranslation(
        string value,
        string tableName,
        string key)
    {
        return string.Equals(value, GetTextForLanguage("eng", tableName, key), StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, GetTextForLanguage("zhs", tableName, key), StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, GetText(tableName, key), StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> GetDirectIndexedKeysForLanguage(
        string language,
        string tableName,
        string keyPrefix)
    {
        IReadOnlyDictionary<string, string> table = GetTable(language, tableName);
        List<(int Index, string Key)> indexedKeys = [];

        foreach (string key in table.Keys)
        {
            if (!key.StartsWith(keyPrefix, StringComparison.Ordinal))
                continue;

            ReadOnlySpan<char> suffix = key.AsSpan(keyPrefix.Length);
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                continue;

            indexedKeys.Add((index, key));
        }

        indexedKeys.Sort(static (left, right) =>
        {
            int indexComparison = left.Index.CompareTo(right.Index);
            return indexComparison != 0
                ? indexComparison
                : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        });

        return indexedKeys.Select(item => item.Key).ToArray();
    }

    private static bool TryGetActiveLocText(
        string tableName,
        string key,
        IReadOnlyList<(string Name, object Value)> variables,
        out string? text)
    {
        try
        {
            LocTable table = LocManager.Instance.GetTable(tableName);
            if (!table.HasEntry(key))
            {
                text = null;
                return false;
            }

            LocString loc = new(tableName, key);
            foreach ((string name, object value) in variables)
                loc.AddObj(name, value);

            text = loc.GetFormattedText();
            return true;
        }
        catch
        {
            text = null;
            return false;
        }
    }

    private static string? GetRawText(string language, string tableName, string key)
    {
        IReadOnlyDictionary<string, string> table = GetTable(language, tableName);
        return table.TryGetValue(key, out string? value) ? value : null;
    }

    private static IReadOnlyDictionary<string, string> GetTable(string language, string tableName)
    {
        string cacheKey = language + "/" + tableName;
        if (TableCache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, string>? cached))
            return cached;

        Dictionary<string, string> loaded = new(StringComparer.Ordinal);
        string path = $"{LocalizationRoot}/{language}/{tableName}.json";

        try
        {
            Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                try
                {
                    string json = file.GetAsText();
                    Dictionary<string, string>? parsed =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (parsed != null)
                    {
                        foreach (KeyValuePair<string, string> pair in parsed)
                            loaded[pair.Key] = pair.Value;
                    }
                }
                finally
                {
                    file.Dispose();
                }
            }
        }
        catch
        {
            // The caller returns the key if even the English resource cannot be read.
        }

        TableCache[cacheKey] = loaded;
        return loaded;
    }

    private static string FormatTemplate(
        string template,
        IReadOnlyList<(string Name, object Value)> variables)
    {
        string formatted = template;
        foreach ((string name, object value) in variables.OrderByDescending(
                     variable => variable.Name.Length))
        {
            string replacement = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            formatted = formatted.Replace("{" + name + "}", replacement, StringComparison.Ordinal);
        }

        return formatted;
    }
}
