using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public readonly record struct SecondRoundTextContext(
    int NextActIndex,
    string ReactionAncientId,
    string? SuppressedAncientId);


public static class ChooseTheAncientBaseAncientText
{
    /*
     * Edit second-round banner overrides here.
     * Any ancient not listed here falls back to: $"{ancientId} Reveals Offerings"
     */
    private static readonly Dictionary<string, string> SecondRoundBannerTextOverrides = new()
    {
        ["DARV"] = "Darv Asks For A Roundtrip",
        ["OROBAS"] = "Orobas Wants To Help",
        ["PAEL"] = "Pael Becomes Sleepy",
        ["TEZCATARA"] = "Tezcatara still burns",
        ["NONUPEIPE"] = "No mortal shall defy Nonupeipe",
        ["TANX"] = "Tanx Won't Give Up Without A Fight",
        ["VAKUU"] = "VAKUU Slides Up With An Offer",
        ["NEOW"] = "Neow Reappears",
    };

    /*
     * Edit second-round dialogue lists here.
     * Each ancient can have multiple lines; the picker uses a deterministic RNG.
     * Any ancient not listed here falls back to the default dialogue list below.
     */
    private static readonly IReadOnlyList<string> DefaultSecondRoundDialogueOptions =
    [
        "You must know my offerings."
    ];

    private static readonly Dictionary<string, IReadOnlyList<string>> SecondRoundDialogueOptionsByAncientId = new()
    {
        ["DARV"] = [
                "These trinkets toppled the Spire a millennium ago."
        ],
        ["OROBAS"] = [
                "But Orobas Brings So Many Great Gifts?"
        ],
        ["PAEL"] = [
                "Take a piece of me so I can go back to sleep."
        ],
        ["TEZCATARA"] = [
                "You could burn so bright with these."
        ],
        ["NONUPEIPE"] = [
                "What's not to like.",
                "Even {SuppressedAncientId} couldn't match this offer."
        ],
        ["TANX"] = [
                "Come on, let's fight."
        ],
        ["VAKUU"] =
            [
                "Let's cut a deal",
            ],
        ["NEOW"] = [
                "I will always offer you my assistance."
        ] 
    };

    public static string GetSecondRoundBannerText(
        string ancientId,
        SecondRoundTextContext context)
    {
        string template = SecondRoundBannerTextOverrides.TryGetValue(ancientId, out string? overrideText)
            ? overrideText
            : "{ReactionAncientId} Reveals Offerings";

        return FormatTemplate(template, context);
    }

    public static string GetSecondRoundDialogueText(
        RunState runState,
        int nextActIndex,
        string reactionAncientId,
        string? suppressedAncientId)
    {
        IReadOnlyList<string> options =
            SecondRoundDialogueOptionsByAncientId.TryGetValue(reactionAncientId, out IReadOnlyList<string>? mapped)
                ? mapped
                : DefaultSecondRoundDialogueOptions;

        var rng = CreateSecondRoundAncientDialoguePickerRng(
            runState,
            nextActIndex,
            reactionAncientId,
            suppressedAncientId);

        string template = options.Count > 0
            ? options[rng.NextInt(options.Count)]
            : "You must know my offerings.";

        return FormatTemplate(template, new SecondRoundTextContext(
            nextActIndex,
            reactionAncientId,
            suppressedAncientId));
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

    private static IReadOnlyList<string> GetSecondRoundDialogueOptions(string ancientId)
    {
        if (SecondRoundDialogueOptionsByAncientId.TryGetValue(ancientId, out IReadOnlyList<string>? dialogueOptions) &&
            dialogueOptions.Count > 0)
        {
            return dialogueOptions;
        }

        return DefaultSecondRoundDialogueOptions;
    }
    
    private static string FormatTemplate(string template, SecondRoundTextContext context)
    {
        return template
            .Replace("{ReactionAncientId}", context.ReactionAncientId)
            .Replace("{SuppressedAncientId}", context.SuppressedAncientId ?? "that ancient")
            .Replace("{ActNumber}", (context.NextActIndex + 1).ToString());
    }
}
