using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace ChooseTheAncient.ChooseTheAncientCode.Interop;

internal static class AncientConfigsPlusInterop
{
    private const string ConfigTypeName = "AncientConfigsPlus.AncientConfigsPlusCode.AncientConfigsPlusConfig";
    private const int DecimalWeightScale = 1000;

    internal static IReadOnlyDictionary<string, int>? TryParseWeights(int slot)
    /*
     * Reads AncientConfigsPlus' saved per-act weights without taking a hard dependency on the mod.
     *
     * AncientConfigsPlus keeps ParseWeights private and currently returns Dictionary<string, decimal>,
     * so CTA reads it by reflection and normalizes positive decimal weights into integer weights for
     * CTA's weighted ballot sampler.
     */
    {
        try
        {
            Type? configType = AccessTools.TypeByName(ConfigTypeName);
            if (configType == null)
            {
                ModLog.Debug("AncientConfigsPlus config type was not found; CTA will use normal ballot limiting.");
                return null;
            }

            MethodInfo? parseWeights = AccessTools.Method(configType, "ParseWeights", [typeof(int)]);
            if (parseWeights == null)
            {
                ModLog.Warn("AncientConfigsPlus was found, but ParseWeights(int) was not found; CTA will use normal ballot limiting.");
                return null;
            }

            object? result = parseWeights.Invoke(null, [slot]);
            Dictionary<string, int>? weights = ConvertWeightDictionary(result);

            if (weights == null)
            {
                ModLog.Warn(
                    $"AncientConfigsPlus ParseWeights({slot}) returned {result?.GetType().FullName ?? "<null>"}; " +
                    "CTA will use normal ballot limiting.");
                return null;
            }

            ModLog.Info(
                $"AncientConfigsPlus interop loaded {weights.Count} configured weight(s) for Act {slot} " +
                $"from {result?.GetType().FullName ?? "<null>"}: " +
                string.Join(",", weights.Select(kv => $"{kv.Key}:{kv.Value}")));

            return weights;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"AncientConfigsPlus interop failed for Act {slot}; CTA will use normal ballot limiting. {ex}");
            return null;
        }
    }

    private static Dictionary<string, int>? ConvertWeightDictionary(object? result)
    {
        if (result is null)
            return null;

        if (result is IReadOnlyDictionary<string, int> readOnlyStringInt)
            return new Dictionary<string, int>(readOnlyStringInt, StringComparer.Ordinal);

        if (result is IDictionary<string, int> stringInt)
            return new Dictionary<string, int>(stringInt, StringComparer.Ordinal);

        if (result is IReadOnlyDictionary<string, decimal> readOnlyStringDecimal)
            return ConvertDecimalDictionary(readOnlyStringDecimal);

        if (result is IDictionary<string, decimal> stringDecimal)
            return ConvertDecimalDictionary(stringDecimal);

        Dictionary<string, int>? fromNonGenericDictionary = ConvertNonGenericDictionary(result);
        if (fromNonGenericDictionary != null)
            return fromNonGenericDictionary;

        Dictionary<string, int>? fromKeyValueEnumerable = ConvertKeyValueEnumerable(result);
        if (fromKeyValueEnumerable != null)
            return fromKeyValueEnumerable;

        return null;
    }

    private static Dictionary<string, int> ConvertDecimalDictionary(IEnumerable<KeyValuePair<string, decimal>> source)
    {
        Dictionary<string, int> weights = new(StringComparer.Ordinal);
        foreach ((string key, decimal value) in source)
            weights[key] = NormalizeDecimalWeight(value);

        return weights;
    }

    private static Dictionary<string, int>? ConvertNonGenericDictionary(object result)
    {
        if (result is not IDictionary dictionary)
            return null;

        Dictionary<string, int> weights = new(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                continue;

            weights[key] = NormalizeWeight(entry.Value);
        }

        return weights;
    }

    private static Dictionary<string, int>? ConvertKeyValueEnumerable(object result)
    {
        if (result is not IEnumerable enumerable || result is string)
            return null;

        Dictionary<string, int> weights = new(StringComparer.Ordinal);
        bool readAnyEntry = false;

        foreach (object? entry in enumerable)
        {
            if (entry == null)
                continue;

            Type entryType = entry.GetType();
            PropertyInfo? keyProperty = entryType.GetProperty("Key");
            PropertyInfo? valueProperty = entryType.GetProperty("Value");

            if (keyProperty?.GetValue(entry) is not string key)
                continue;

            weights[key] = NormalizeWeight(valueProperty?.GetValue(entry));
            readAnyEntry = true;
        }

        return readAnyEntry ? weights : null;
    }

    private static int NormalizeWeight(object? value)
    {
        if (value == null)
            return 0;

        try
        {
            if (value is decimal decimalValue)
                return NormalizeDecimalWeight(decimalValue);

            if (value is double doubleValue)
                return NormalizeDecimalWeight((decimal)doubleValue);

            if (value is float floatValue)
                return NormalizeDecimalWeight((decimal)floatValue);

            if (value is IConvertible convertible)
                return NormalizeDecimalWeight(convertible.ToDecimal(CultureInfo.InvariantCulture));

            return decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                ? NormalizeDecimalWeight(parsed)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int NormalizeDecimalWeight(decimal value)
    {
        if (value <= 0m)
            return 0;

        decimal scaled = value * DecimalWeightScale;
        if (scaled >= int.MaxValue)
            return int.MaxValue;

        return Math.Max(1, (int)Math.Ceiling(scaled));
    }
}
