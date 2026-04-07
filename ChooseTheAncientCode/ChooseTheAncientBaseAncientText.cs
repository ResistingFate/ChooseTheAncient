using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public readonly record struct AncientTextContext(
    int NextActIndex,
    string ReactionAncientId,
    string? SuppressedAncientId);

public static class ChooseTheAncientBaseAncientText
{
    // Must use base-game loc table names so STS2 actually loads the mod's JSON files.
    // Generic UI goes in gameplay_ui.json, ancient-specific lines go in ancients.json.
    private const string UiTableName = "gameplay_ui";
    private const string AncientTableName = "ancients";

    public static string GetInitialRoundBannerText(int nextActIndex)
    {
        LocString loc = new(UiTableName, "choose_the_ancient.round_intro.initial_keep_vote");
        loc.Add("ActLabel", GetActLabelText(nextActIndex));
        loc.Add("ActNumber", (nextActIndex + 1).ToString());
        return SafeFormat(loc, $"Choose the Act {nextActIndex + 1} Ancients");
    }

    public static string GetSecondRoundBannerText(AncientTextContext context)
    {
        string key = AncientKeyExists($"choose_the_ancient.round_intro.final_reveal.{context.ReactionAncientId}")
            ? $"choose_the_ancient.round_intro.final_reveal.{context.ReactionAncientId}"
            : "choose_the_ancient.round_intro.final_reveal.default";

        LocString loc = new(AncientTableName, key);
        AddContextVariables(loc, context);
        return SafeFormat(loc, $"{context.ReactionAncientId} Reveals Offerings");
    }

    public static string GetSecondRoundDialogueText(RunState? runState, AncientTextContext context)
    {
        Rng? rng = runState == null
            ? null
            : CreateSecondRoundAncientDialoguePickerRng(
                runState,
                context.NextActIndex,
                context.ReactionAncientId,
                context.SuppressedAncientId);

        LocString loc = GetDialogueLocString(context.ReactionAncientId, rng)
            ?? new LocString(AncientTableName, "choose_the_ancient.second_round.dialogue.default.0");

        AddContextVariables(loc, context);
        return SafeFormat(loc, "You must know my offerings.");
    }

    public static Rng CreateSecondRoundAncientDialoguePickerRng(
        RunState runState,
        int nextActIndex,
        string reactionAncientId,
        string? suppressedAncientId)
    {
        string suppressedPart = string.IsNullOrWhiteSpace(suppressedAncientId)
            ? "none"
            : suppressedAncientId;

        return new Rng(
            runState.Rng.Seed,
            $"choose_the_ancient_second_round_dialogue_{nextActIndex}_{reactionAncientId}_{suppressedPart}");
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

    private static string GetActLabelText(int nextActIndex)
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

    private static LocString? GetDialogueLocString(string reactionAncientId, Rng? rng)
    {
        string specificPrefix = $"choose_the_ancient.second_round.dialogue.{reactionAncientId}.";
        if (AncientPrefixExists(specificPrefix))
        {
            return rng == null
                ? TryGetFirstLocStringWithPrefix(AncientTableName, specificPrefix)
                : LocString.GetRandomWithPrefix(AncientTableName, specificPrefix, rng);
        }

        const string defaultPrefix = "choose_the_ancient.second_round.dialogue.default.";
        if (AncientPrefixExists(defaultPrefix))
        {
            return rng == null
                ? TryGetFirstLocStringWithPrefix(AncientTableName, defaultPrefix)
                : LocString.GetRandomWithPrefix(AncientTableName, defaultPrefix, rng);
        }

        return null;
    }

    private static LocString? TryGetFirstLocStringWithPrefix(string tableName, string keyPrefix)
    {
        LocTable? table = TryGetTable(tableName);
        if (table == null)
            return null;

        IReadOnlyList<LocString> options = table.GetLocStringsWithPrefix(keyPrefix);
        return options.Count > 0 ? options[0] : null;
    }

    private static void AddContextVariables(LocString loc, AncientTextContext context)
    {
        loc.Add("ReactionAncientId", context.ReactionAncientId);
        loc.Add("SuppressedAncientId", context.SuppressedAncientId ?? "that ancient");
        loc.Add("ActNumber", (context.NextActIndex + 1).ToString());
        loc.Add("ActLabel", GetActLabelText(context.NextActIndex));
    }

    private static string SafeFormat(LocString loc, string fallback)
    {
        try
        {
            return loc.GetFormattedText();
        }
        catch
        {
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

    private static bool AncientPrefixExists(string keyPrefix)
    {
        LocTable? table = TryGetTable(AncientTableName);
        return table != null && table.GetLocStringsWithPrefix(keyPrefix).Count > 0;
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
