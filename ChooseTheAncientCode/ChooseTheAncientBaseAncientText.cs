using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChooseTheAncient.ChooseTheAncientCode.Interop;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public readonly record struct AncientTextContext(
    int NextActIndex,
    string ReactionAncientEntry,
    string? ReactionAncientTitle,
    string? SuppressedAncientEntry,
    string? SuppressedAncientTitle,
    string? CharacterEntry = null,
    string? CharacterTitle = null,
    string? ActEntry = null,
    string? ActTitle = null);

public static class ChooseTheAncientBaseAncientText
{
    // Must use base-game loc table names so STS2 actually loads the mod's JSON files.
    // Generic UI goes in gameplay_ui.json, ancient-specific lines go in ancients.json.
    private const string UiTableName = "gameplay_ui";
    private const string AncientTableName = "ancients";
    private const string SecondRoundDialogueRoot = "choose_the_ancient.second_round.dialogue.";

    public static string GetInitialRoundBannerText(int nextActIndex)
    {
        LocString loc = new(UiTableName, "choose_the_ancient.round_intro.initial_keep_vote");
        loc.Add("ActLabel", GetNumberedActLabelText(nextActIndex));
        loc.Add("ActNumber", (nextActIndex + 1).ToString(CultureInfo.InvariantCulture));
        return SafeFormat(loc, $"Choose the Act {nextActIndex + 1} Ancients");
    }

    public static string GetSecondRoundBannerText(AncientTextContext context)
    {
        return GetSecondRoundBannerText(null, context);
    }

    public static string GetSecondRoundBannerText(RunState? runState, AncientTextContext context)
    {
        context = ResolveRuntimeActContext(runState, context);
        string key = GetSecondRoundBannerLocKey(context);

        LocString loc = new(AncientTableName, key);
        AddFinalRevealVariables(loc, context);

        return SafeFormat(
            loc,
            $"{context.ReactionAncientTitle ?? context.ReactionAncientEntry} Offers");
    }

    public static string GetSecondRoundDialogueText(RunState? runState, AncientTextContext context)
    {
        return GetSecondRoundDialogueText(
            runState,
            context,
            DialogueSpeakerRole.Reaction);
    }

    public static string GetSuppressedSecondRoundDialogueText(
        RunState? runState,
        AncientTextContext context)
    {
        return GetSecondRoundDialogueText(
            runState,
            context,
            DialogueSpeakerRole.Suppressed);
    }

    private static string GetSecondRoundDialogueText(
        RunState? runState,
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        context = ResolveRuntimeActContext(runState, context);

        string? speakerEntry = GetSpeakerAncientEntry(context, speakerRole);
        string? otherAncientEntry = GetOtherAncientEntry(context, speakerRole);

        Rng? rng = runState == null || string.IsNullOrWhiteSpace(speakerEntry)
            ? null
            : CreateSecondRoundAncientDialoguePickerRng(
                runState,
                context.NextActIndex,
                speakerRole,
                speakerEntry!,
                otherAncientEntry);

        LocString? loc = GetDialogueLocString(
            context,
            speakerRole,
            rng,
            out bool usedDefaultDialogue);

        if (loc == null)
        {
            string warning = BuildMissingDialogueAndDefaultWarning(context, speakerRole);
            ModLog.Warn(warning);
            return warning;
        }

        AddDialogueVariables(
            loc,
            context,
            speakerRole);

        string formattingWarning = BuildInvalidDialogueWarning(
            context,
            speakerRole,
            loc.LocEntryKey);
        string formattedText = SafeFormat(loc, formattingWarning, out Exception? formattingError);
        if (formattingError != null)
        {
            ModLog.Warn($"{formattingWarning} Formatting failed: {formattingError.Message}");
            return formattedText;
        }

        ModLog.Debug(
            $"Second-round dialogue resolved: role={speakerRole}, " +
            $"speaker={speakerEntry ?? "<none>"}, other={otherAncientEntry ?? "<none>"}, " +
            $"key={loc.LocEntryKey}, default={usedDefaultDialogue}, text={formattedText}");

        return formattedText;
    }

    public static Rng CreateSecondRoundAncientDialoguePickerRng(
        RunState runState,
        int nextActIndex,
        string reactionAncientEntry,
        string? suppressedAncientEntry)
    {
        return CreateSecondRoundAncientDialoguePickerRng(
            runState,
            nextActIndex,
            DialogueSpeakerRole.Reaction,
            reactionAncientEntry,
            suppressedAncientEntry);
    }

    private static Rng CreateSecondRoundAncientDialoguePickerRng(
        RunState runState,
        int nextActIndex,
        DialogueSpeakerRole speakerRole,
        string speakerAncientEntry,
        string? otherAncientEntry)
    {
        string otherPart = string.IsNullOrWhiteSpace(otherAncientEntry)
            ? "none"
            : otherAncientEntry;
        string rolePart = speakerRole == DialogueSpeakerRole.Suppressed
            ? "suppressed"
            : "reaction";

        return ChooseTheAncientHelpers.CreateRunScopedRng(
            runState,
            "second_round_dialogue",
            rolePart,
            nextActIndex,
            speakerAncientEntry,
            otherPart);
    }

    public static string GetVoteForThisAncientButtonText() =>
        GetUiText("choose_the_ancient.button.vote_for_this_ancient", "Vote For This Ancient");

    public static string GetSelectedAncientButtonText() =>
        GetUiText("choose_the_ancient.button.selected_ancient", "Selected Ancient");

    public static string GetVotingClosedButtonText() =>
        GetUiText("choose_the_ancient.button.voting_closed", "Voting Closed");

    public static string GetVoteLockedButtonText() =>
        GetUiText("choose_the_ancient.button.vote_locked", "Vote Locked");

    public static string GetUnavailableButtonText() =>
        GetUiText("choose_the_ancient.button.unavailable", "Unavailable");

    private static string GetNumberedActLabelText(int nextActIndex)
    {
        int actNumber = nextActIndex + 1;
        return GetUiText($"choose_the_ancient.act_label.{actNumber}", $"Act {actNumber}");
    }

    private static string GetUiText(string key, string fallback)
    {
        if (!UiKeyExists(key))
            return fallback;

        return SafeFormat(new LocString(UiTableName, key), fallback);
    }

    private static string GetSecondRoundBannerLocKey(
        AncientTextContext context)
    {
        string specificKey =
            $"choose_the_ancient.round_intro.final_reveal." +
            context.ReactionAncientEntry;

        return AncientKeyExists(specificKey)
            ? specificKey
            : "choose_the_ancient.round_intro.final_reveal.default";
    }

    private static LocString? GetDialogueLocString(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole,
        Rng? rng,
        out bool usedDefaultDialogue)
    {
        string? speakerEntry = GetSpeakerAncientEntry(context, speakerRole);
        if (!string.IsNullOrWhiteSpace(speakerEntry))
        {
            string? otherAncientEntry =
                GetOtherAncientEntry(context, speakerRole);

            DialogueLocalizationLookupContext lookupContext = new(
                SpeakerRole: speakerRole,
                SpeakerAncientEntry: speakerEntry!,
                OtherAncientEntry: otherAncientEntry,
                CharacterEntry: context.CharacterEntry,
                ActEntry: context.ActEntry);

            ChooseTheAncientDialogueBranchContext branchContext = new(
                SpeakerAncientEntry: speakerEntry!,
                OtherAncientEntry: otherAncientEntry,
                CharacterEntry: context.CharacterEntry,
                ActEntry: context.ActEntry,
                IsSuppressedDialogue:
                    speakerRole == DialogueSpeakerRole.Suppressed);

            foreach (ResolvedDialogueBranch branch in
                     ChooseTheAncientApi.ResolveDialogueBranches(
                         branchContext))
            {
                foreach (string prefix in
                         ChooseTheAncientDialogueLocalizationRules
                             .BuildBranchPrefixSearchOrder(
                                 SecondRoundDialogueRoot,
                                 lookupContext,
                                 branch.Name,
                                 branch.Value))
                {
                    LocString? loc =
                        GetDirectIndexedLocString(prefix, rng);
                    if (loc != null)
                    {
                        usedDefaultDialogue = false;
                        return loc;
                    }
                }
            }

            foreach (string prefix in
                     ChooseTheAncientDialogueLocalizationRules
                         .BuildPrefixSearchOrder(
                             SecondRoundDialogueRoot,
                             lookupContext))
            {
                LocString? loc = GetDirectIndexedLocString(prefix, rng);
                if (loc != null)
                {
                    usedDefaultDialogue = false;
                    return loc;
                }
            }
        }

        string defaultPrefix =
            ChooseTheAncientDialogueLocalizationRules.BuildDefaultPrefix(
                SecondRoundDialogueRoot,
                speakerRole);
        LocString? defaultLoc = GetDirectIndexedLocString(defaultPrefix, rng);
        usedDefaultDialogue = defaultLoc != null;
        return defaultLoc;
    }

    private static string? GetSpeakerAncientEntry(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        return speakerRole == DialogueSpeakerRole.Suppressed
            ? context.SuppressedAncientEntry
            : context.ReactionAncientEntry;
    }

    private static string? GetOtherAncientEntry(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        return speakerRole == DialogueSpeakerRole.Suppressed
            ? context.ReactionAncientEntry
            : context.SuppressedAncientEntry;
    }

    private static LocString? GetDirectIndexedLocString(string keyPrefix, Rng? rng)
    {
        IReadOnlyList<LocString> options = GetDirectIndexedLocStrings(AncientTableName, keyPrefix);
        if (options.Count == 0)
            return null;

        return rng == null
            ? options[0]
            : rng.NextItem(options);
    }

    internal static IReadOnlyList<LocString> GetDirectIndexedLocStrings(
        string tableName,
        string keyPrefix)
    {
        LocTable? table = TryGetTable(tableName);
        if (table == null)
            return Array.Empty<LocString>();

        List<(int Index, LocString Loc)> indexedOptions = [];

        foreach (LocString loc in table.GetLocStringsWithPrefix(keyPrefix))
        {
            if (!table.IsLocalKey(loc.LocEntryKey))
                continue;

            if (ChooseTheAncientDialogueLocalizationRules.TryGetDirectNumericIndex(
                    loc.LocEntryKey,
                    keyPrefix,
                    out int index))
                indexedOptions.Add((index, loc));
        }

        indexedOptions.Sort(static (left, right) =>
        {
            int indexComparison = left.Index.CompareTo(right.Index);
            return indexComparison != 0
                ? indexComparison
                : string.Compare(left.Loc.LocEntryKey, right.Loc.LocEntryKey, StringComparison.Ordinal);
        });

        return indexedOptions.Select(option => option.Loc).ToList();
    }

    private static AncientTextContext ResolveRuntimeActContext(
        RunState? runState,
        AncientTextContext context)
    {
        ActModel? act = TryGetTargetAct(runState, context.NextActIndex);
        if (act == null)
            return context;

        string actEntry = act.Id.Entry;
        string actTitle = SafeFormat(act.Title, actEntry);

        return context with
        {
            ActEntry = actEntry,
            ActTitle = actTitle
        };
    }

    private static ActModel? TryGetTargetAct(RunState? runState, int nextActIndex)
    {
        if (runState == null || nextActIndex < 0 || nextActIndex >= runState.Acts.Count)
            return null;

        try
        {
            return runState.Acts[nextActIndex];
        }
        catch (Exception ex)
        {
            ModLog.Warn(
                $"Could not resolve ActModel at runState.Acts[{nextActIndex}] for dialogue localization: {ex.Message}");
            return null;
        }
    }

    private static void AddFinalRevealVariables(
        LocString loc,
        AncientTextContext context)
    {
        Dictionary<string, object> variables =
            BuildBuiltInVariables(context, DialogueSpeakerRole.Reaction);

        AddVariablesToLocString(loc, variables);
    }

    private static void AddDialogueVariables(
        LocString loc,
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        Dictionary<string, object> variables =
            BuildBuiltInVariables(context, speakerRole);

        AddVariablesToLocString(loc, variables);
    }

    private static Dictionary<string, object> BuildBuiltInVariables(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        string reactionAncient =
            context.ReactionAncientTitle
            ?? context.ReactionAncientEntry;
        string suppressedAncient =
            context.SuppressedAncientTitle
            ?? context.SuppressedAncientEntry
            ?? "UNKNOWN_ANCIENT";
        string character =
            context.CharacterTitle
            ?? context.CharacterEntry
            ?? "UNKNOWN_CHARACTER";
        string actTitle =
            context.ActTitle
            ?? context.ActEntry
            ?? GetNumberedActLabelText(context.NextActIndex);

        string speakerAncient =
            speakerRole == DialogueSpeakerRole.Suppressed
                ? suppressedAncient
                : reactionAncient;
        string otherAncient =
            speakerRole == DialogueSpeakerRole.Suppressed
                ? reactionAncient
                : suppressedAncient;

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["SpeakerAncient"] = speakerAncient,
            ["OtherAncient"] = otherAncient,
            ["Character"] = character,
            ["ActTitle"] = actTitle
        };
    }

    private static void AddVariablesToLocString(
        LocString loc,
        IReadOnlyDictionary<string, object> variables)
    {
        foreach (KeyValuePair<string, object> pair in variables.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            loc.AddObj(pair.Key, pair.Value);
        }
    }

    private static string BuildMissingDialogueAndDefaultWarning(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole)
    {
        string language = TryGetActiveLanguage() ?? "UNKNOWN_LANGUAGE";
        string roleSegment = speakerRole == DialogueSpeakerRole.Suppressed
            ? "suppressed"
            : "reaction";
        string speakerEntry = GetSpeakerAncientEntry(context, speakerRole)
            ?? "UNKNOWN_ANCIENT";
        string defaultPrefix =
            ChooseTheAncientDialogueLocalizationRules.BuildDefaultPrefix(
                SecondRoundDialogueRoot,
                speakerRole);

        return
            $"WARNING: ChooseTheAncient found no {roleSegment} second-round dialogue " +
            $"for ancient '{speakerEntry}' and no {roleSegment} default dialogue for " +
            $"language '{language}'. No defaults activated. This language should be supported. " +
            $"Add '{defaultPrefix}0' to this locale's ancients.json.";
    }

    private static string BuildInvalidDialogueWarning(
        AncientTextContext context,
        DialogueSpeakerRole speakerRole,
        string locEntryKey)
    {
        string roleSegment = speakerRole == DialogueSpeakerRole.Suppressed
            ? "suppressed"
            : "reaction";
        string speakerEntry = GetSpeakerAncientEntry(context, speakerRole)
            ?? "UNKNOWN_ANCIENT";

        return
            $"WARNING: ChooseTheAncient could not format {roleSegment} second-round " +
            $"dialogue localization '{locEntryKey}' for ancient '{speakerEntry}'.";
    }

    private static string? TryGetActiveLanguage()
    {
        try
        {
            return LocManager.Instance.Language;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFormat(LocString loc, string fallback)
    {
        return SafeFormat(loc, fallback, out _);
    }

    private static string SafeFormat(
        LocString loc,
        string fallback,
        out Exception? formattingError)
    {
        try
        {
            formattingError = null;
            return loc.GetFormattedText();
        }
        catch (Exception ex)
        {
            formattingError = ex;
            return fallback;
        }
    }

    private static bool UiKeyExists(string key)
    {
        LocTable? table = TryGetTable(UiTableName);
        return table?.HasEntry(key) ?? false;
    }

    private static bool AncientKeyExists(string key)
    {
        LocTable? table = TryGetTable(AncientTableName);
        return table?.HasEntry(key) ?? false;
    }

    private static LocTable? TryGetTable(string tableName)
    {
        try
        {
            return LocManager.Instance.GetTable(tableName);
        }
        catch
        {
            return null;
        }
    }
}
