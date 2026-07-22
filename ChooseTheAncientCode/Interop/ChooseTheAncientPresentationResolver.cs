using System;
using System.Globalization;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ChooseTheAncient.ChooseTheAncientCode.Interop;

internal static class ChooseTheAncientPresentationResolver
{
    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    internal static ChooseTheAncientPresentation ResolveVisualPresentation(AncientEventModel ancient)
    {
        return ReadConventionProperties(ancient);
    }

    internal static Color GetDialogueColor(AncientEventModel ancient)
    {
        ChooseTheAncientPresentation presentation = ResolveVisualPresentation(ancient);
        return presentation.DialogueColor ?? ancient.DialogueColor;
    }

    internal static Color? GetAccentColor(AncientEventModel ancient)
    {
        return ResolveVisualPresentation(ancient).AccentColor;
    }

    internal static bool TryGetSecondRoundDialogueLocPrefix(
        AncientEventModel? ancient,
        string ancientId,
        string? characterId,
        int nextActIndex,
        string? suppressedAncientId,
        out string prefix)
    {
        if (ancient == null)
        {
            prefix = string.Empty;
            return false;
        }

        ChooseTheAncientPresentation presentation = ReadConventionProperties(ancient);
        if (!string.IsNullOrWhiteSpace(presentation.SecondRoundDialogueLocPrefix))
        {
            prefix = presentation.SecondRoundDialogueLocPrefix!;
            return true;
        }

        prefix = string.Empty;
        return false;
    }

    internal static bool TryGetFinalRevealBannerLocKey(
        AncientEventModel? ancient,
        string ancientId,
        string? characterId,
        int nextActIndex,
        string? suppressedAncientId,
        out string key)
    {
        if (ancient == null)
        {
            key = string.Empty;
            return false;
        }

        ChooseTheAncientPresentation presentation = ReadConventionProperties(ancient);
        if (!string.IsNullOrWhiteSpace(presentation.FinalRevealBannerLocKey))
        {
            key = presentation.FinalRevealBannerLocKey!;
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static ChooseTheAncientPresentation ReadConventionProperties(AncientEventModel ancient)
    {
        Type type = ancient.GetType();

        return new ChooseTheAncientPresentation
        {
            PortalScale = TryReadFloatProperty(type, ancient, "ChooseTheAncientPortalScale"),
            PortalBaseSize = TryReadVector2Property(type, ancient, "ChooseTheAncientPortalBaseSize"),
            PortalSourceAnchor = TryReadVector2Property(type, ancient, "ChooseTheAncientPortalSourceAnchor"),
            PortalExtraOffset = TryReadVector2Property(type, ancient, "ChooseTheAncientPortalExtraOffset"),
            AccentColor =
                TryReadColorProperty(type, ancient, "ChooseTheAncientAccentColor")
                ?? TryReadColorHexProperty(type, ancient, "ChooseTheAncientAccentHex"),
            DialogueColor =
                TryReadColorProperty(type, ancient, "ChooseTheAncientDialogueColor")
                ?? TryReadColorHexProperty(type, ancient, "ChooseTheAncientDialogueColorHex"),
            SecondRoundDialogueLocPrefix =
                ChooseTheAncientPresentationHelpers.NormalizeLocPrefix(
                    TryReadStringProperty(type, ancient, "ChooseTheAncientSecondRoundDialoguePrefix")),
            FinalRevealBannerLocKey =
                ChooseTheAncientPresentationHelpers.NormalizeLocKey(
                    TryReadStringProperty(type, ancient, "ChooseTheAncientFinalRevealBannerKey"))
        };
    }

    private static float? TryReadFloatProperty(Type type, object instance, string propertyName)
    {
        if (!TryReadPropertyValue(type, instance, propertyName, out object? value) || value == null)
            return null;

        try
        {
            return value switch
            {
                float f => f,
                double d => (float)d,
                decimal m => (float)m,
                int i => i,
                long l => l,
                IConvertible convertible => convertible.ToSingle(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static Vector2? TryReadVector2Property(Type type, object instance, string propertyName)
    {
        return TryReadPropertyValue(type, instance, propertyName, out object? value) && value is Vector2 vector
            ? vector
            : null;
    }

    private static Color? TryReadColorProperty(Type type, object instance, string propertyName)
    {
        return TryReadPropertyValue(type, instance, propertyName, out object? value) && value is Color color
            ? color
            : null;
    }

    private static Color? TryReadColorHexProperty(Type type, object instance, string propertyName)
    {
        return ChooseTheAncientPresentationHelpers.TryParseColor(
            TryReadStringProperty(type, instance, propertyName));
    }

    private static string? TryReadStringProperty(Type type, object instance, string propertyName)
    {
        return TryReadPropertyValue(type, instance, propertyName, out object? value)
            ? value as string
            : null;
    }

    private static bool TryReadPropertyValue(
        Type type,
        object instance,
        string propertyName,
        out object? value)
    {
        value = null;

        try
        {
            PropertyInfo? property = type.GetProperty(propertyName, InstanceMemberFlags);
            if (property == null || property.GetIndexParameters().Length != 0)
                return false;

            value = property.GetValue(instance);
            return true;
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                $"Ignoring {propertyName} on {type.FullName} because reading it failed: {UnwrapReflectionException(ex)}");
            return false;
        }
    }

    private static Exception UnwrapReflectionException(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner }
            ? inner
            : ex;
    }
}
