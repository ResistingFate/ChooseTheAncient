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
        return ChooseTheAncientLocalization.GetText(
            UiTableName,
            "choose_the_ancient.round_intro.initial_keep_vote",
            ("ActLabel", GetNumberedActLabelText(nextActIndex)),
            ("ActNumber", (nextActIndex + 1).ToString(CultureInfo.InvariantCulture)));
    }

    public static string GetSecondRoundBannerText(AncientTextContext context)
    {
        return GetSecondRoundBannerText(null, context);
    }

    public static string GetSecondRoundBannerText(RunState? runState, AncientTextContext context)
    {
        context = ResolveRuntimeActContext(runState, context);
        string key = GetSecondRoundBannerLocKey(context);

        Dictionary<string, object> variables =
            BuildBuiltInVariables(context, DialogueSpeakerRole.Reaction);

        return ChooseTheAncientLocalization.GetText(
            AncientTableName,
            key,
            variables.Select(pair => (pair.Key, pair.Value)).ToArray());
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
            return string.Empty;
        }

        Dictionary<string, object> variables =
            BuildBuiltInVariables(context, speakerRole);
        AddVariablesToLocString(loc, variables);

        string englishFallback = ChooseTheAncientLocalization.GetTextForLanguage(
            "eng",
            AncientTableName,
            loc.LocEntryKey,
            variables.Select(pair => (pair.Key, pair.Value)).ToArray());

        string formattingWarning = BuildInvalidDialogueWarning(
            context,
            speakerRole,
            loc.LocEntryKey);
        string formattedText = SafeFormat(loc, englishFallback, out Exception? formattingError);
        if (formattingError != null)
            ModLog.Warn($"{formattingWarning} Formatting failed: {formattingError.Message}");

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
        GetUiText("choose_the_ancient.button.vote_for_this_ancient");

    public static string GetSelectedAncientButtonText() =>
        GetUiText("choose_the_ancient.button.selected_ancient");

    public static string GetVotingClosedButtonText() =>
        GetUiText("choose_the_ancient.button.voting_closed");

    public static string GetVoteLockedButtonText() =>
        GetUiText("choose_the_ancient.button.vote_locked");

    public static string GetUnavailableButtonText() =>
        GetUiText("choose_the_ancient.button.unavailable");

    internal static string GetStreamerVoteLabel(bool isFinalRevealVote) =>
        GetUiText(
            isFinalRevealVote
                ? "choose_the_ancient.streamer.vote_label.final_reveal"
                : "choose_the_ancient.streamer.vote_label.standard");

    internal static string GetStreamerAncientFallbackLabel(int index) =>
        ChooseTheAncientLocalization.GetText(
            UiTableName,
            "choose_the_ancient.streamer.fallback.ancient",
            ("Index", index.ToString(CultureInfo.InvariantCulture)));

    internal static string GetStreamerOptionFallbackLabel(int index) =>
        ChooseTheAncientLocalization.GetText(
            UiTableName,
            "choose_the_ancient.streamer.fallback.option",
            ("Index", index.ToString(CultureInfo.InvariantCulture)));

    private static string GetNumberedActLabelText(int nextActIndex)
    {
        int actNumber = nextActIndex + 1;
        return GetUiText($"choose_the_ancient.act_label.{actNumber}");
    }

    private static string GetUiText(string key)
    {
        return ChooseTheAncientLocalization.GetText(UiTableName, key);
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
        List<(string Prefix, bool IsDefault)> searchOrder = [];
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
                    searchOrder.Add((prefix, false));
                }
            }

            foreach (string prefix in
                     ChooseTheAncientDialogueLocalizationRules
                         .BuildPrefixSearchOrder(
                             SecondRoundDialogueRoot,
                             lookupContext))
            {
                searchOrder.Add((prefix, false));
            }
        }

        string defaultPrefix =
            ChooseTheAncientDialogueLocalizationRules.BuildDefaultPrefix(
                SecondRoundDialogueRoot,
                speakerRole);
        searchOrder.Add((defaultPrefix, true));

        LocString? localLoc = GetFirstAvailableDialogueLocString(
            searchOrder,
            rng,
            useEnglishTable: false,
            out usedDefaultDialogue);
        if (localLoc != null)
            return localLoc;

        return GetFirstAvailableDialogueLocString(
            searchOrder,
            rng,
            useEnglishTable: true,
            out usedDefaultDialogue);
    }

    private static LocString? GetFirstAvailableDialogueLocString(
        IReadOnlyList<(string Prefix, bool IsDefault)> searchOrder,
        Rng? rng,
        bool useEnglishTable,
        out bool usedDefaultDialogue)
    {
        foreach ((string prefix, bool isDefault) in searchOrder)
        {
            IReadOnlyList<LocString> options = useEnglishTable
                ? GetEnglishDirectIndexedLocStrings(prefix)
                : GetDirectIndexedLocStrings(AncientTableName, prefix);

            LocString? selected = SelectIndexedLocString(options, rng);
            if (selected == null)
                continue;

            usedDefaultDialogue = isDefault;
            return selected;
        }

        usedDefaultDialogue = false;
        return null;
    }

    private static LocString? SelectIndexedLocString(
        IReadOnlyList<LocString> options,
        Rng? rng)
    {
        if (options.Count == 0)
            return null;

        return rng == null
            ? options[0]
            : rng.NextItem(options);
    }

    private static IReadOnlyList<LocString> GetEnglishDirectIndexedLocStrings(
        string keyPrefix)
    {
        IReadOnlyList<string> englishKeys =
            ChooseTheAncientLocalization.GetDirectIndexedKeysForLanguage(
                "eng",
                AncientTableName,
                keyPrefix);

        return englishKeys
            .Select(key => new LocString(AncientTableName, key))
            .ToArray();
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
                : string.Compare(
                    left.Loc.LocEntryKey,
                    right.Loc.LocEntryKey,
                    StringComparison.Ordinal);
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
            $"ChooseTheAncient found no {roleSegment} second-round dialogue for " +
            $"ancient '{speakerEntry}', and the English fallback '{defaultPrefix}0' is missing.";
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

    private static bool AncientKeyExists(string key)
    {
        return ChooseTheAncientLocalization.HasActiveOrEnglishText(
            AncientTableName,
            key);
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
