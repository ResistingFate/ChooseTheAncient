using System;
using ChooseTheAncient.Scripts;
using Godot;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal static class ChooseTheAncientConfig
{
    public enum VoteClickTargetMode
    {
        ButtonOnly = 0,
        WholeCard = 1,
        WholeSlot = 2,
    }

    private const int DefaultAncientCount = 3;
    private const bool DefaultShowControllerHotkeys = false;
    private const bool DefaultShowOnlyButtonOutline = true;
    private const VoteClickTargetMode DefaultVoteClickTarget = VoteClickTargetMode.ButtonOnly;

    public static int AncientCount { get; private set; } = DefaultAncientCount;
    public static bool ShowControllerHotkeys { get; private set; } = DefaultShowControllerHotkeys;
    public static bool ShowOnlyButtonOutline { get; private set; } = DefaultShowOnlyButtonOutline;
    public static VoteClickTargetMode VoteClickTarget { get; private set; } = DefaultVoteClickTarget;

    public static void RefreshFromModConfig()
    {
        AncientCount = NormalizeAncientCount(
            ModConfigBridge.GetValue("ancientCount", (float)DefaultAncientCount));

        ShowControllerHotkeys =
            ModConfigBridge.GetValue("showControllerHotkeys", DefaultShowControllerHotkeys);

        ShowOnlyButtonOutline =
            ModConfigBridge.GetValue("showOnlyButtonOutline", DefaultShowOnlyButtonOutline);

        VoteClickTarget = NormalizeVoteClickTarget(
            ModConfigBridge.GetValue("voteClickTarget", (float)(int)DefaultVoteClickTarget));
    }

    public static void ApplyAncientCount(object value)
    {
        AncientCount = NormalizeAncientCount(value);
    }

    public static void ApplyShowControllerHotkeys(object value)
    {
        ShowControllerHotkeys = Convert.ToBoolean(value);
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }

    public static void ApplyShowOnlyButtonOutlineHotkeys(object value)
    {
        ShowOnlyButtonOutline = Convert.ToBoolean(value);
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }

    public static void ApplyVoteClickTarget(object value)
    {
        VoteClickTarget = NormalizeVoteClickTarget(value);
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }

    private static int NormalizeAncientCount(object value)
    {
        int count = value switch
        {
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => DefaultAncientCount
        };

        return Math.Clamp(count, 2, 8);
    }

    private static VoteClickTargetMode NormalizeVoteClickTarget(object value)
    {
        int rawValue = value switch
        {
            VoteClickTargetMode mode => (int)mode,
            int i => i,
            long l => (int)l,
            float f => Mathf.RoundToInt(f),
            double d => (int)Math.Round(d),
            _ => (int)DefaultVoteClickTarget
        };

        rawValue = Math.Clamp(rawValue, (int)VoteClickTargetMode.ButtonOnly, (int)VoteClickTargetMode.WholeSlot);
        return (VoteClickTargetMode)rawValue;
    }
}
