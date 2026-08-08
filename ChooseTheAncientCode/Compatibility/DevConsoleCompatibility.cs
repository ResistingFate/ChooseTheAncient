using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

/// <summary>
/// Avoids using NDevConsole.IsConsoleVisible, which is not present on sts2 0.107.1 main.
/// </summary>
internal static class DevConsoleCompatibility
{
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? InstanceField =
        typeof(NDevConsole).GetField("_instance", StaticFlags);

    private static readonly PropertyInfo? InstanceProperty =
        typeof(NDevConsole).GetProperty("Instance", StaticFlags);

    private static readonly MethodInfo? HideConsoleMethod =
        typeof(NDevConsole).GetMethod(
            "HideConsole",
            InstanceFlags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

    private static bool _reportedFailure;

    public static void HideIfVisible()
    {
        try
        {
            NDevConsole? console = TryGetInstance();
            if (console == null
                || !GodotObject.IsInstanceValid(console)
                || !console.Visible)
            {
                return;
            }

            if (HideConsoleMethod == null)
            {
                ReportFailure(
                    "Could not find NDevConsole.HideConsole().");
                return;
            }

            HideConsoleMethod.Invoke(console, null);
        }
        catch (Exception ex)
        {
            ReportFailure(
                $"Could not hide the dev console. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static NDevConsole? TryGetInstance()
    {
        if (InstanceField?.GetValue(null) is NDevConsole fieldInstance)
        {
            return fieldInstance;
        }

        try
        {
            return InstanceProperty?.GetValue(null) as NDevConsole;
        }
        catch
        {
            return null;
        }
    }

    private static void ReportFailure(string message)
    {
        if (_reportedFailure)
        {
            return;
        }

        _reportedFailure = true;
        ModLog.Warn(message);
    }
}
