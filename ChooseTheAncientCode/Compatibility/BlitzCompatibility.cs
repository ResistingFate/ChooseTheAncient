using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

internal static class BlitzCompatibility
{
    private static readonly Assembly GameAssembly = typeof(RunState).Assembly;
    private static readonly Type? BlitzRunModelType =
        GameAssembly.GetType("MegaCrit.Sts2.Core.Models.Blitz.BlitzRunModel");
    private static readonly Type? BlitzPointType =
        GameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.CommonUi.NBlitzTopBarMapPoint");
    private static readonly PropertyInfo? RunModels = typeof(RunState).GetProperty("RunModels");
    private static readonly PropertyInfo? Point = BlitzPointType?.GetProperty("Point");
    private static readonly MethodInfo? UpdateIcon = BlitzPointType == null
        ? null
        : AccessTools.Method(BlitzPointType, "UpdateIcon", Type.EmptyTypes);
    private static readonly Func<Player, Task>? GiveNeowRewards = ResolveDraft();

    private static Func<Player, Task>? ResolveDraft()
    {
        if (BlitzRunModelType == null || RunModels?.GetGetMethod() == null ||
            RunModels.GetSetMethod(nonPublic: true) == null)
        {
            return null;
        }

        MethodInfo? method = BlitzRunModelType.GetMethod(
            "GiveNeowRewards", BindingFlags.Public | BindingFlags.Static,
            binder: null, types: [typeof(Player)], modifiers: null);
        return method?.ReturnType == typeof(Task)
            ? (Func<Player, Task>)method.CreateDelegate(typeof(Func<Player, Task>))
            : null;
    }

    public static bool IsBlitzRun(RunState runState) =>
        BlitzRunModelType != null &&
        RunModels?.GetValue(runState) is IEnumerable models &&
        models.Cast<object>().Any(BlitzRunModelType.IsInstanceOfType);

    public static bool CanDraft(RunState runState) =>
        GiveNeowRewards != null && IsBlitzRun(runState);

    /// <summary>
    /// Starts the native draft for every player. The game's choice
    /// synchronizer opens and receives the remote selection.
    /// </summary>
    public static async Task DraftAsync(IReadOnlyList<Player> players)
    {
        await Task.WhenAll(players.Select(player => GiveNeowRewards!(player)));
    }

    /// <summary>
    /// Removes only Blitz from the synchronous Neow option-generation scope.
    /// Returns the exact list to restore when the original call exits.
    /// </summary>
    public static object? MaskRunModel(RunState runState)
    {
        if (!CanDraft(runState) || RunModels!.GetValue(runState) is not
            IEnumerable original)
        {
            return null;
        }

        object[] remaining = original.Cast<object>()
            .Where(model => !BlitzRunModelType!.IsInstanceOfType(model)).ToArray();
        Type modelType = RunModels.PropertyType.GetGenericArguments()[0];
        Array filtered = Array.CreateInstance(modelType, remaining.Length);
        Array.Copy(remaining, filtered, remaining.Length);
        RunModels.SetValue(runState, filtered);
        return original;
    }

    public static void RestoreRunModels(RunState runState, object original) =>
        RunModels!.SetValue(runState, original);

    public static void PatchTopBarIcon(Harmony harmony)
    {
        if (UpdateIcon != null && Point != null)
        {
            harmony.Patch(UpdateIcon, postfix: new HarmonyMethod(
                typeof(BlitzCompatibility), nameof(ApplyUnresolvedIcon)));
        }
    }

    private static bool IsStartingPoint(Node node, RunState runState) =>
        BlitzPointType?.IsInstanceOfType(node) == true &&
        Point?.GetValue(node) is MapPoint point &&
        point.PointType == MapPointType.Ancient &&
        point.coord == runState.Map.StartingMapPoint.coord;

    private static void ApplyUnresolvedIcon(Node __instance)
    {
        RunState? runState = ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
        if (runState == null || !IsStartingPoint(__instance, runState))
            return;

        ChooseTheAncientFlowState flow = ChooseTheAncientStateStore.Get(runState);
        if (!ChooseTheAncientHelpers.ShouldPrepareUnresolvedStartingAncientNode(
                runState, flow, runState.CurrentActIndex))
        {
            return;
        }

        string iconName = NNormalMapPoint.IconName(MapPointType.Unknown);
        var icon = GD.Load<Texture2D>(NNormalMapPoint.IconPath(iconName));
        var outline = GD.Load<Texture2D>(NNormalMapPoint.OutlinePath(iconName));
        if (icon != null && outline != null)
            SetTextures(__instance, icon, outline);
    }

    /// <summary>
    /// Updates the existing Blitz starting node after a ballot without rebuilding
    /// the top bar or disturbing its current-position marker.
    /// </summary>
    public static bool RefreshChosenIcon(RunState runState, AncientEventModel ancient)
    {
        var topBar = NRun.Instance?.GlobalUi?.TopBar;
        if (topBar == null || BlitzPointType == null)
            return false;

        return RefreshChosenIcon(topBar, runState, ancient);
    }

    private static bool RefreshChosenIcon(Node parent, RunState runState, AncientEventModel ancient)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (IsStartingPoint(child, runState))
            {
                string? iconPath = ImageHelper.GetRoomIconPath(
                    MapPointType.Ancient, RoomType.Event, ancient.Id);
                string? outlinePath = ImageHelper.GetRoomIconOutlinePath(
                    MapPointType.Ancient, RoomType.Event, ancient.Id);

                return SetTextures(
                    child,
                    iconPath == null ? null : ResourceLoader.Load<Texture2D>(iconPath),
                    outlinePath == null ? null : ResourceLoader.Load<Texture2D>(outlinePath));
            }

            if (RefreshChosenIcon(child, runState, ancient))
                return true;
        }

        return false;
    }

    private static bool SetTextures(Node point, Texture2D? icon, Texture2D? outline)
    {
        TextureRect? iconNode = point.GetNodeOrNull<TextureRect>("%Icon");
        TextureRect? outlineNode = point.GetNodeOrNull<TextureRect>("%Outline");
        if (iconNode == null || outlineNode == null)
            return false;

        iconNode.Texture = icon;
        outlineNode.Texture = outline;
        return true;
    }
}
