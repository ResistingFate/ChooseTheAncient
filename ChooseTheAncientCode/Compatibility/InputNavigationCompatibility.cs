using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

/// <summary>
/// Avoids binding CTA directly to the input-navigation property renamed in STS2 0.110.0.
/// </summary>
internal static class InputNavigationCompatibility
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly PropertyInfo? NavigationModeProperty =
        typeof(NControllerManager).GetProperty("IsUsingDirectionalNavigation", InstanceFlags)
        ?? typeof(NControllerManager).GetProperty("IsUsingController", InstanceFlags);

    private static readonly PropertyInfo? InputTypeProperty =
        typeof(NControllerManager).GetProperty("InputType", InstanceFlags);

    private static bool _reportedMissingProperty;
    private static bool _reportedInputTypeReadFailure;

    public static bool IsUsingDirectionalNavigation(NControllerManager controllerManager)
    {
        if (NavigationModeProperty == null)
        {
            if (!_reportedMissingProperty)
            {
                _reportedMissingProperty = true;
                ModLog.Warn(
                    "Could not find NControllerManager.IsUsingDirectionalNavigation or " +
                    "NControllerManager.IsUsingController. CTA input glyph prompts will remain hidden.");
            }

            return false;
        }

        try
        {
            return NavigationModeProperty.GetValue(controllerManager) is true;
        }
        catch (Exception ex)
        {
            if (!_reportedMissingProperty)
            {
                _reportedMissingProperty = true;
                ModLog.Warn(
                    $"Could not read NControllerManager.{NavigationModeProperty.Name}; " +
                    $"CTA input glyph prompts will remain hidden. {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }

    /// <summary>
    /// Returns whether STS2 is in the 0.110+ keyboard-only navigation
    /// mode as a check to maintain compatability with the last sts2
    /// 0.107.1 main.
    /// </summary>
    public static bool IsKeyboardOnlyMode(NControllerManager controllerManager)
    {
        if (InputTypeProperty == null)
            return false;

        try
        {
            object? inputType = InputTypeProperty.GetValue(controllerManager);
            return string.Equals(
                inputType?.ToString(),
                "KeyboardOnlyMode",
                StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            if (!_reportedInputTypeReadFailure)
            {
                _reportedInputTypeReadFailure = true;
                ModLog.Warn(
                    $"Could not read NControllerManager.{InputTypeProperty.Name}; " +
                    $"CTA will use controller-sized input glyph layout. {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }
}
