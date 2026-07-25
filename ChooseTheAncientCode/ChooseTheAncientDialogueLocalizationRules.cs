using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal enum DialogueSpeakerRole
{
    Reaction,
    Suppressed
}

internal readonly record struct DialogueLocalizationLookupContext(
    DialogueSpeakerRole SpeakerRole,
    string SpeakerAncientEntry,
    string? OtherAncientEntry,
    string? CharacterEntry,
    string? ActEntry);

internal static class ChooseTheAncientDialogueLocalizationRules
{
    private readonly record struct DialoguePrefixPattern(
        bool UsesOtherAncient,
        bool UsesCharacter,
        bool UsesAct);

    private static readonly DialoguePrefixPattern[] _prefixSearchOrder =
    {
        new(true, true, true),
        new(true, true, false),
        new(true, false, true),
        new(true, false, false),
        new(false, true, true),
        new(false, true, false),
        new(false, false, true),
        new(false, false, false)
    };

    internal static IEnumerable<string> BuildPrefixSearchOrder(
        string dialogueRoot,
        DialogueLocalizationLookupContext context)
    {
        return BuildPrefixSearchOrder(
            dialogueRoot,
            context,
            branchName: null,
            branchValue: null);
    }

    internal static IEnumerable<string> BuildBranchPrefixSearchOrder(
        string dialogueRoot,
        DialogueLocalizationLookupContext context,
        string branchName,
        string branchValue)
    {
        return BuildPrefixSearchOrder(
            dialogueRoot,
            context,
            NormalizeRequiredSegment(branchName, nameof(branchName)),
            NormalizeRequiredSegment(branchValue, nameof(branchValue)));
    }

    internal static string BuildDefaultPrefix(
        string dialogueRoot,
        DialogueSpeakerRole speakerRole)
    {
        string root = NormalizeLocPrefix(dialogueRoot);
        string roleSegment =
            speakerRole == DialogueSpeakerRole.Suppressed
                ? "suppressed"
                : "reaction";

        return $"{root}{roleSegment}.default.";
    }

    internal static bool TryGetDirectNumericIndex(
        string locEntryKey,
        string keyPrefix,
        out int index)
    {
        index = -1;

        if (!locEntryKey.StartsWith(keyPrefix, StringComparison.Ordinal))
            return false;

        string suffix = locEntryKey[keyPrefix.Length..];
        if (suffix.Length == 0 || suffix.Contains('.'))
            return false;

        return int.TryParse(
                   suffix,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out index)
               && index >= 0;
    }

    private static IEnumerable<string> BuildPrefixSearchOrder(
        string dialogueRoot,
        DialogueLocalizationLookupContext context,
        string? branchName,
        string? branchValue)
    {
        string root = NormalizeLocPrefix(dialogueRoot);
        string roleSegment =
            context.SpeakerRole == DialogueSpeakerRole.Suppressed
                ? "suppressed"
                : "reaction";
        string speakerEntry = NormalizeRequiredSegment(
            context.SpeakerAncientEntry,
            nameof(context.SpeakerAncientEntry));

        string speakerPrefix = $"{root}{roleSegment}.{speakerEntry}.";
        if (branchName != null && branchValue != null)
            speakerPrefix += $"{branchName}.{branchValue}.";

        string? otherAncientEntry =
            NormalizeEntrySegment(context.OtherAncientEntry);
        string? characterEntry =
            NormalizeEntrySegment(context.CharacterEntry);
        string? actEntry =
            NormalizeEntrySegment(context.ActEntry);

        foreach (DialoguePrefixPattern pattern in _prefixSearchOrder)
        {
            if (pattern.UsesOtherAncient && otherAncientEntry == null)
                continue;

            if (pattern.UsesCharacter && characterEntry == null)
                continue;

            if (pattern.UsesAct && actEntry == null)
                continue;

            string prefix = speakerPrefix;

            if (pattern.UsesOtherAncient)
                prefix += $"other_ancient.{otherAncientEntry}.";

            if (pattern.UsesCharacter)
                prefix += $"character.{characterEntry}.";

            if (pattern.UsesAct)
                prefix += $"act.{actEntry}.";

            yield return prefix;
        }
    }

    private static string NormalizeLocPrefix(string prefix)
    {
        string trimmed = prefix.Trim();
        return trimmed.EndsWith(".", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ".";
    }

    private static string NormalizeRequiredSegment(
        string segment,
        string parameterName)
    {
        string? normalized = NormalizeEntrySegment(segment);
        if (normalized == null)
        {
            throw new ArgumentException(
                "A localization key segment is required.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeEntrySegment(string? entry)
    {
        return string.IsNullOrWhiteSpace(entry)
            ? null
            : entry.Trim();
    }
}
