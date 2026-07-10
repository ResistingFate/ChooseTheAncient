using System;
using Godot;

namespace ChooseTheAncient.ChooseTheAncientCode.Interop;

/// <summary>
/// Optional public integration point for mods that want to explicitly tune how their
/// Ancient appears on Choose The Ancient's selection screen.
/// </summary>
internal sealed record ChooseTheAncientPresentation
{
    public float? PortalScale { get; init; }
    public Vector2? PortalSourceAnchor { get; init; }
    public Vector2? PortalExtraOffset { get; init; }
    public Vector2? PortalBaseSize { get; init; }
    public string? PortalSourceNodePath { get; init; }
    public bool? PortalAutoDetectSourceNode { get; init; }

    public Color? AccentColor { get; init; }
    public Color? DialogueColor { get; init; }

    public string? SecondRoundDialogueLocPrefix { get; init; }
    public string? FinalRevealBannerLocKey { get; init; }
}

internal static class ChooseTheAncientPresentationHelpers
{
    internal static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try
        {
            return new Color(hex);
        }
        catch
        {
            return null;
        }
    }


    internal static string? NormalizeNodePath(string? nodePath)
    {
        return string.IsNullOrWhiteSpace(nodePath) ? null : nodePath.Trim();
    }

    internal static string? NormalizeLocPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        string trimmed = prefix.Trim();
        return trimmed.EndsWith(".", StringComparison.Ordinal) ? trimmed : trimmed + ".";
    }

    internal static string? NormalizeLocKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }
}
