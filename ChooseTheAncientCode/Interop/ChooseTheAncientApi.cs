using System;
using System.Collections.Generic;
using Godot;

namespace ChooseTheAncient.ChooseTheAncientCode.Interop;

/// <summary>
/// Records provided for dialogue-branch code.
/// </summary>
public readonly record struct ChooseTheAncientDialogueBranchContext(
    string SpeakerAncientEntry,
    string? OtherAncientEntry,
    string? CharacterEntry,
    string? ActEntry,
    bool IsSuppressedDialogue);

public delegate string? ChooseTheAncientDialogueBranchResolver(
    ChooseTheAncientDialogueBranchContext context);

internal readonly record struct ResolvedDialogueBranch(
    string Name,
    string Value);

/// <summary>
/// API for mods that add higher-priority dialogue branches.
/// Register each branch once during mod initialization.
/// </summary>
public static class ChooseTheAncientApi
{
    private static readonly SortedDictionary<
        string,
        ChooseTheAncientDialogueBranchResolver>
        _dialogueBranchResolvers = new(StringComparer.Ordinal);

    private static readonly HashSet<string> _reservedDialogueSegments =
        new(StringComparer.Ordinal)
        {
            "reaction",
            "suppressed",
            "default",
            "other_ancient",
            "character",
            "act"
        };

    public static void RegisterDialogueBranch(
        string branchName,
        ChooseTheAncientDialogueBranchResolver resolver)
    {
        string normalizedName = NormalizeBranchSegment(
            branchName,
            nameof(branchName));

        if (_reservedDialogueSegments.Contains(normalizedName))
        {
            throw new ArgumentException(
                $"'{normalizedName}' is reserved by the dialogue localization schema.",
                nameof(branchName));
        }

        ArgumentNullException.ThrowIfNull(resolver);

        if (_dialogueBranchResolvers.ContainsKey(normalizedName))
        {
            throw new InvalidOperationException(
                $"A ChooseTheAncient dialogue branch named " +
                $"'{normalizedName}' is already registered.");
        }

        _dialogueBranchResolvers.Add(normalizedName, resolver);
    }

    internal static IEnumerable<ResolvedDialogueBranch> ResolveDialogueBranches(
        ChooseTheAncientDialogueBranchContext context)
    {
        foreach ((string branchName,
                     ChooseTheAncientDialogueBranchResolver resolver)
                 in _dialogueBranchResolvers)
        {
            string? branchValue;

            try
            {
                branchValue = resolver(context);
            }
            catch (Exception ex)
            {
                ModLog.Warn(
                    $"ChooseTheAncient dialogue branch " +
                    $"'{branchName}' failed: {ex}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(branchValue))
                continue;

            string normalizedValue;
            try
            {
                normalizedValue = NormalizeBranchSegment(
                    branchValue,
                    "branchValue");
            }
            catch (ArgumentException ex)
            {
                ModLog.Warn(
                    $"ChooseTheAncient dialogue branch " +
                    $"'{branchName}' returned an invalid value: " +
                    ex.Message);
                continue;
            }

            yield return new ResolvedDialogueBranch(
                branchName,
                normalizedValue);
        }
    }

    private static string NormalizeBranchSegment(
        string segment,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException(
                "Dialogue branch names and values cannot be empty.",
                parameterName);
        }

        string normalized = segment.Trim();
        if (normalized.Contains('.'))
        {
            throw new ArgumentException(
                "Dialogue branch names and values cannot contain '.'.",
                parameterName);
        }

        return normalized;
    }
}

/// <summary>
/// Optional convention-backed presentation values for custom ancients.
/// Dialogue and final-reveal localization keys are discovered directly from
/// the merged ancients localization table using the ancient's runtime Id.Entry.
/// </summary>
internal sealed record ChooseTheAncientPresentation
{
    public float? PortalScale { get; init; }

    public Vector2? PortalBaseSize { get; init; }
    public Vector2? PortalSourceAnchor { get; init; }
    public Vector2? PortalExtraOffset { get; init; }

    public Color? AccentColor { get; init; }
    public Color? DialogueColor { get; init; }
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
}
