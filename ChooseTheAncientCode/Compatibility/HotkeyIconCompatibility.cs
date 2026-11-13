using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

/// <summary>
/// Keeps CTA from using the NHotkeyIcon type for sts2 main 0.107.1
/// On older branches, vote-button prompts are simply hidden.
/// </summary>
internal static class HotkeyIconCompatibility
{
    private const string HotkeyIconTypeName =
        "MegaCrit.Sts2.Core.Nodes.CommonUi.NHotkeyIcon";

    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type? HotkeyIconType =
        typeof(NControllerManager).Assembly.GetType(
            HotkeyIconTypeName,
            throwOnError: false);

    private static readonly MethodInfo? UpdateInputMethod =
        HotkeyIconType?.GetMethod(
            "UpdateInput",
            InstanceFlags,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

    private static readonly MethodInfo? LegacyGetHotkeyIconMethod =
        typeof(NInputManager).GetMethod(
            "GetHotkeyIcon",
            InstanceFlags,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

    private static bool _reportedUnavailable;
    private static bool _reportedUpdateFailure;
    private static bool _reportedInstantiationFailure;
    private static bool _reportedLegacyLookupFailure;

    public static PackedScene? TryLoadScene(string scenePath)
    {
        if (HotkeyIconType == null)
        {
            ReportUnavailable(
                "NHotkeyIcon is not present in this STS2 build");
            return null;
        }

        if (!ResourceLoader.Exists(scenePath))
        {
            ReportUnavailable(
                $"the hotkey icon scene does not exist at {scenePath}");
            return null;
        }

        PackedScene? scene = GD.Load<PackedScene>(scenePath);
        if (scene == null)
        {
            ReportUnavailable(
                $"the hotkey icon scene could not be loaded from {scenePath}");
        }

        return scene;
    }

    public static Control CreateLegacyControllerPrompt()
    {
        return new TextureRect
        {
            Name = "LegacyControllerHotkeyIcon",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
    }

    public static bool UpdateInput(Control icon, StringName input)
    {
        if (UpdateInputMethod != null
            && HotkeyIconType != null
            && HotkeyIconType.IsInstanceOfType(icon))
        {
            try
            {
                UpdateInputMethod.Invoke(icon, [input.ToString()]);
                return true;
            }
            catch (Exception ex)
            {
                ReportUpdateFailure(ex);
                return false;
            }
        }

        if (icon is not TextureRect legacyIcon)
        {
            return false;
        }

        if (LegacyGetHotkeyIconMethod == null || NInputManager.Instance == null)
        {
            ReportLegacyLookupFailure(
                "NInputManager.GetHotkeyIcon(string) is unavailable");
            return false;
        }

        try
        {
            Texture2D? texture =
                LegacyGetHotkeyIconMethod.Invoke(
                    NInputManager.Instance,
                    [input.ToString()]) as Texture2D;

            legacyIcon.Texture = texture;
            return texture != null;
        }
        catch (Exception ex)
        {
            ReportLegacyLookupFailure(
                $"{ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    public static void ReportInstantiationFailure(Exception ex)
    {
        if (_reportedInstantiationFailure)
        {
            return;
        }

        _reportedInstantiationFailure = true;
        ModLog.Warn(
            "Could not instantiate the CTA 0.110+ vote-button input glyph. " +
            $"CTA will try its legacy controller-only prompt instead. {ex.GetType().Name}: {ex.Message}");
    }

    private static void ReportUpdateFailure(Exception ex)
    {
        if (_reportedUpdateFailure)
        {
            return;
        }

        _reportedUpdateFailure = true;
        ModLog.Warn(
            "Could not update the CTA 0.110+ vote-button input glyph. " +
            $"The prompt will remain hidden or stale. {ex.GetType().Name}: {ex.Message}");
    }

    private static void ReportLegacyLookupFailure(string reason)
    {
        if (_reportedLegacyLookupFailure)
        {
            return;
        }

        _reportedLegacyLookupFailure = true;
        ModLog.Warn(
            $"Could not update CTA's legacy controller prompt because {reason}. " +
            "Controller navigation will continue without the glyph.");
    }

    private static void ReportUnavailable(string reason)
    {
        if (_reportedUnavailable)
        {
            return;
        }

        _reportedUnavailable = true;
        ModLog.Info(
            $"CTA's 0.110+ adaptive vote-button prompt is unavailable because {reason}. " +
            "CTA will use the legacy controller-only glyph path when directional navigation is active.");
    }
}
