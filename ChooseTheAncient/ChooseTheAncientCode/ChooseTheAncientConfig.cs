using System;
using ChooseTheAncient.Scripts;
using Godot;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal static class ChooseTheAncientConfig
{
    private const int DefaultAncientCount = 3;
    private const bool DefaultShowControllerHotkeys = false;
    private const bool DefaultShowOnlyButtonOutline = true;

    public static int AncientCount { get; private set; } = DefaultAncientCount;
    public static bool ShowControllerHotkeys { get; private set; } = DefaultShowControllerHotkeys;
    public static bool ShowOnlyButtonOutline { get; private set; } = DefaultShowOnlyButtonOutline;

    public static void RefreshFromModConfig()
    {
        AncientCount = NormalizeAncientCount(
            ModConfigBridge.GetValue("ancientCount", 3.0f));

        ShowControllerHotkeys =
            ModConfigBridge.GetValue("showControllerHotkeys", DefaultShowControllerHotkeys);
        
        ShowOnlyButtonOutline =
            ModConfigBridge.GetValue("showOnlyButtonOutline", DefaultShowOnlyButtonOutline);
    }

    public static void ApplyAncientCount(object value)
    {
        AncientCount = NormalizeAncientCount(value);
    }

    public static void ApplyShowControllerHotkeys(object value)
    {
        ShowControllerHotkeys = Convert.ToBoolean(value);

        // Only needed if you want an already-open screen to update immediately.
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }
    
    public static void ApplyShowOnlyButtonOutlineHotkeys(object value)
    {
        ShowOnlyButtonOutline = Convert.ToBoolean(value);

        // Only needed if you want an already-open screen to update immediately.
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }

    // Use this only when your mod changes the value outside the ModConfig menu.
    /*public static void SetShowControllerHotkeysOutsideMenu(bool value)
    {
        ShowControllerHotkeys = value;
        ModConfigBridge.SetValue("showControllerHotkeys", value);
        AncientBanSelectionScreen.RefreshModConfigHotkeys();
    }*/

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
}