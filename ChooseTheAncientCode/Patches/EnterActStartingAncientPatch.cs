using System.Diagnostics;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ChooseTheAncient.ChooseTheAncientCode.Rooms;

namespace ChooseTheAncient.ChooseTheAncientCode.Patches;

/// <summary>
/// Makes Acts 2+ use CTA's unresolved starting-Ancient shell without replacing
/// RunManager.EnterAct by marking an EnterAct, then replaces only the one MapRoom
/// entry that uses SetActInternal with a direct EnterMapCoord(startingCoord).
/// </summary>
public static class EnterActStartingAncientPatch
{
    private const int PresentationWaitMaxFrames = 1200;

    private sealed class EnterActPatchState
    {
        public required ChooseTheAncientFlowState Flow { get; init; }
        public required int ActIndex { get; init; }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterAct))]
    private static class EnterActIntentPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            RunManager __instance,
            int currentActIndex,
            out EnterActPatchState? __state)
        {
            __state = null;

            RunState? runState =
                ChooseTheAncientHelpers.GetRunState(__instance);

            if (runState == null
                || currentActIndex <= 0
                || currentActIndex >= runState.Acts.Count)
            {
                return;
            }

            ChooseTheAncientFlowState flow =
                ChooseTheAncientStateStore.Get(runState);

            // Explicit console navigation owns its requested act. Re-entering a
            // resolved act should keep vanilla/debug behavior as well.
            if (flow.ConsoleNavigationInProgress
                || flow.FlowInProgress
                || flow.ResolvedActs.Contains(currentActIndex))
            {
                return;
            }

            flow.PendingVanillaMapRoomReplacementActIndex =
                currentActIndex;

            __state = new EnterActPatchState
            {
                Flow = flow,
                ActIndex = currentActIndex,
            };

            ModLog.Debug(
                $"CTA marked vanilla EnterAct for Act {currentActIndex + 1} " +
                "to replace only its pending MapRoom entry with the unresolved " +
                "starting Ancient.");
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ref Task __result,
            EnterActPatchState? __state)
        {
            if (__state == null)
                return;

            __result = FinalizeVanillaEnterActAsync(
                __result,
                __state);
        }
    }

    [HarmonyPatch(typeof(RunManager), "EnterRoomInternal")]
    private static class EnterRoomInternalMapReplacementPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(
            RunManager __instance,
            AbstractRoom room,
            bool isRestoringRoomStackBase,
            ref Task __result)
        {
            if (room is not MapRoom || isRestoringRoomStackBase)
                return true;

            RunState? runState =
                ChooseTheAncientHelpers.GetRunState(__instance);
            if (runState == null)
                return true;

            ChooseTheAncientFlowState flow =
                ChooseTheAncientStateStore.Get(runState);

            int actIndex = runState.CurrentActIndex;
            if (flow.PendingVanillaMapRoomReplacementActIndex
                != actIndex)
            {
                return true;
            }

            if (!ChooseTheAncientHelpers
                    .ShouldPrepareUnresolvedStartingAncientNode(
                        runState,
                        flow,
                        actIndex)
                || runState.Map.StartingMapPoint.PointType
                    != MapPointType.Ancient)
            {
                flow.PendingVanillaMapRoomReplacementActIndex =
                    null;

                ModLog.Warn(
                    $"CTA declined Act {actIndex + 1}'s pending MapRoom " +
                    "replacement because the generated starting point was not " +
                    "an unresolved Ancient. Vanilla MapRoom entry will continue.");
                return true;
            }

            // Consume the marker before EnterMapCoord.
            flow.PendingVanillaMapRoomReplacementActIndex =
                null;

            __result =
                EnterStartingAncientInsteadOfMapRoomAsync(
                    __instance,
                    runState,
                    flow,
                    actIndex);

            ModLog.Info(
                $"CTA replaced only vanilla Act {actIndex + 1}'s MapRoom " +
                "entry with the unresolved starting Ancient flow.");

            return false;
        }
    }

    private static async Task EnterStartingAncientInsteadOfMapRoomAsync(
        RunManager runManager,
        RunState runState,
        ChooseTheAncientFlowState flow,
        int actIndex)
    {
        Stopwatch? stopwatch =
            ModLog.IsPerformanceTracingEnabled
                ? Stopwatch.StartNew()
                : null;

        MapCoord startingCoord =
            runState.Map.StartingMapPoint.coord;

        flow.ActiveFlowTargetActIndex = actIndex;

        NMapScreen? mapScreen = NMapScreen.Instance;
        mapScreen?.InitMarker(startingCoord);

        ModLog.Info(
            $"CTA is entering Act {actIndex + 1}'s unresolved starting " +
            $"Ancient at {startingCoord} from vanilla's MapRoom seam.");

        await runManager.EnterMapCoord(startingCoord);
        mapScreen?.RefreshAllMapPointVotes();

        await WaitForSelectionPresentationAsync(
            runState,
            flow,
            actIndex);

        if (stopwatch != null)
        {
            stopwatch.Stop();
            ModLog.Trace(
                $"[Perf] Act {actIndex + 1} MapRoom replacement reached " +
                $"CTA's first presentation in " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        }
    }

    private static async Task WaitForSelectionPresentationAsync(
        RunState runState,
        ChooseTheAncientFlowState flow,
        int actIndex)
    {
        for (int frame = 0;
             frame < PresentationWaitMaxFrames;
             frame++)
        {
            if (ChooseTheAncientSelectionScreen
                .IsRoundPresentedForAct(actIndex))
            {
                ModLog.Debug(
                    $"Act {actIndex + 1} CTA round is built and visible; " +
                    $"returning control to vanilla EnterAct after " +
                    $"{frame + 1} process frame(s).");
                return;
            }

            // Empty and single-option ballots may resolve without leaving a
            // visible screen. Wait until their shell replacement has completed.
            if (flow.ResolvedActs.Contains(actIndex)
                && runState.CurrentRoom
                    is not ChooseTheAncientStartRoom)
            {
                ModLog.Debug(
                    $"Act {actIndex + 1} CTA auto-resolved before opening a " +
                    "ballot; returning control to vanilla EnterAct after " +
                    $"{frame + 1} process frame(s).");
                return;
            }

            await ChooseTheAncientHelpers
                .WaitForProcessFramesAsync(1);
        }

        ModLog.Warn(
            $"Act {actIndex + 1} CTA presentation did not report ready " +
            $"within {PresentationWaitMaxFrames} process frames. Returning " +
            "control to vanilla EnterAct to avoid trapping the transition.");
    }

    private static async Task FinalizeVanillaEnterActAsync(
        Task vanillaEnterAct,
        EnterActPatchState state)
    {
        try
        {
            await vanillaEnterAct;
        }
        finally
        {
            if (state.Flow
                    .PendingVanillaMapRoomReplacementActIndex
                == state.ActIndex)
            {
                state.Flow
                    .PendingVanillaMapRoomReplacementActIndex =
                    null;
            }
        }
    }
}
