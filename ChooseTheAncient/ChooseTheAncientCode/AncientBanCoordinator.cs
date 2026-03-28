using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class AncientBanCoordinator
{
    public static async Task RunAsync(
        RunManager runManager,
        RunState runState,
        int nextActIndex,
        AncientBanFlowState flow)
    {
        try
        {
            var nextAct = runState.Acts[nextActIndex];
            List<AncientEventModel> pool = AncientBanHelpers.BuildCandidatePool(nextAct, runState);

            AncientBanHelpers.LogPool($"Act {nextActIndex + 1} ancient candidate pool", pool);

            if (pool.Count <= 1)
            {
                if (pool.Count == 1)
                {
                    AncientBanHelpers.SetChosenAncient(nextAct, pool[0]);
                    GD.Print($"[YourMod] Only one ancient available for act {nextActIndex + 1}: {pool[0].Id.Entry}");
                }

                flow.ResolvedActs.Add(nextActIndex);
                flow.ContinueEnterNextAct = true;
                await runManager.EnterNextAct();
                return;
            }

            // Temporary v1: singleplayer/local vote only.
            // Multiplayer choice sync comes next.
            int bannedIndex = await AncientBanSelectionScreen.ShowAndWait(pool, nextActIndex);

            List<AncientEventModel> remaining = pool
                .Where((_, i) => i != bannedIndex)
                .ToList();
            
            AncientBanHelpers.LogPool($"Act {nextActIndex + 1} remaining after ban", remaining);
            AncientEventModel chosen;
            if (remaining.Count == 1)
            {
                chosen = remaining[0];
            }
            else
            {
                chosen = ResolveSpawnedAncient(runState, nextActIndex, remaining);
            }
            AncientBanHelpers.SetChosenAncient(nextAct, chosen);

            GD.Print($"[ChooseTheAncient] Banned ancient index {bannedIndex}; chosen spawn for act {nextActIndex + 1}: {chosen.Id.Entry}");

            flow.ResolvedActs.Add(nextActIndex);
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ChooseTheAncient] Ancient ban flow failed: {ex}");
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        finally
        {
            flow.FlowInProgress = false;
        }
    }

    private static AncientEventModel ResolveSpawnedAncient(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> remaining)
    {
        if (remaining.Count == 0)
        {
            throw new InvalidOperationException("No ancients remain after ban.");
        }

        if (remaining.Count == 1)
        {
            return remaining[0];
        }

        var rng = AncientBanHelpers.CreateSpawnResolutionRng(runState, nextActIndex);
        return rng.NextItem(remaining)
            ?? throw new InvalidOperationException("Failed to roll remaining ancient.");
    }
}