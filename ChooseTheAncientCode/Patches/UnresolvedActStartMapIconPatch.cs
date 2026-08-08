using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Replaces the native starting Ancient node textures with CTA's unresolved
/// random-Ancient icon until the current act's ballot has been applied.
/// </summary>
[HarmonyPatch(typeof(NAncientMapPoint), nameof(NAncientMapPoint._Ready))]
public static class UnresolvedActStartMapIconPatch
{
    private const string IconPath =
        "res://scenes/mod/choose_the_ancient/map/ancient_node_random.png";

    private const string OutlinePath =
        "res://scenes/mod/choose_the_ancient/map/ancient_node_random_outline.png";

    private static Texture2D? _iconTexture;
    private static Texture2D? _outlineTexture;
    private static bool _loadAttempted;
    private static bool _warnedAboutMissingAssets;
    private static bool _loggedFirstApplication;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(NAncientMapPoint __instance)
    {
        RunState? runState = ChooseTheAncientHelpers.GetRunState(RunManager.Instance);
        if (runState == null || !ShouldUseRandomAncientIcon(__instance, runState))
            return;

        EnsureTexturesLoaded();
        if (_iconTexture == null || _outlineTexture == null)
        {
            WarnOnce(
                "Could not load the Random Ancient map icon assets. " +
                "The unresolved starting point will keep the currently rolled Ancient icon.");
            return;
        }

        TextureRect? icon = __instance.GetNodeOrNull<TextureRect>("Icon");
        TextureRect? outline = __instance.GetNodeOrNull<TextureRect>("Icon/Outline");

        if (icon == null || outline == null)
        {
            WarnOnce(
                "Could not access the native NAncientMapPoint icon nodes. " +
                "The unresolved starting point will keep the currently rolled Ancient icon.");
            return;
        }

        icon.Texture = _iconTexture;
        outline.Texture = _outlineTexture;

        if (!_loggedFirstApplication)
        {
            _loggedFirstApplication = true;
            ModLog.Info(
                "Applied the Random Ancient textures to an unresolved native starting Ancient map point.");
        }
    }

    internal static bool ShouldUseRandomAncientIcon(
        NAncientMapPoint mapPointNode,
        RunState runState)
    {
        int actIndex = runState.CurrentActIndex;
        ChooseTheAncientFlowState flow =
            ChooseTheAncientStateStore.Get(runState);

        if (!ChooseTheAncientHelpers.ShouldPrepareUnresolvedStartingAncientNode(
                runState,
                flow,
                actIndex))
        {
            return false;
        }

        MapPoint startingPoint = runState.Map.StartingMapPoint;
        MapPoint point = mapPointNode.Point;

        return startingPoint.PointType == MapPointType.Ancient
               && point.PointType == MapPointType.Ancient
               && point.coord == startingPoint.coord;
    }

    private static void EnsureTexturesLoaded()
    {
        if (_loadAttempted)
            return;

        _loadAttempted = true;
        _iconTexture = ResourceLoader.Load<Texture2D>(
            IconPath,
            null,
            ResourceLoader.CacheMode.Reuse);

        _outlineTexture = ResourceLoader.Load<Texture2D>(
            OutlinePath,
            null,
            ResourceLoader.CacheMode.Reuse);
    }

    private static void WarnOnce(string message)
    {
        if (_warnedAboutMissingAssets)
            return;

        _warnedAboutMissingAssets = true;
        ModLog.Warn(message);
    }
}
