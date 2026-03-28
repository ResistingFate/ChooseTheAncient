using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
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

            List<int> votesInPlayerSlotOrder = await CollectVotes(runState, pool, nextActIndex);
            int bannedIndex = ResolveBannedIndex(runState, nextActIndex, votesInPlayerSlotOrder);

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
    
    private static async Task<List<int>> CollectVotes(
        RunState runState,
        IReadOnlyList<AncientEventModel> pool,
        int nextActIndex)
    {
        List<Player> orderedPlayers = runState.Players
            .OrderBy(p => runState.GetPlayerSlotIndex(p))
            .ToList();

        Dictionary<ulong, uint> choiceIdsByPlayer = new();

        foreach (Player player in orderedPlayers)
        {
            uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
            choiceIdsByPlayer[player.NetId] = choiceId;
        }

        Task<int>[] voteTasks = orderedPlayers
            .Select(player => GetVoteForPlayer(
                player,
                choiceIdsByPlayer[player.NetId],
                pool,
                nextActIndex))
            .ToArray();

        int[] votes = await Task.WhenAll(voteTasks);

        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            GD.Print($"[ChooseTheAncient] Received vote for player {orderedPlayers[i].NetId}: {votes[i]}");
        }

        return votes.ToList();
    } 

    private static async Task<int> GetVoteForPlayer(
        Player player,
        uint choiceId,
        IReadOnlyList<AncientEventModel> pool,
        int nextActIndex)
    {
        if (ShouldSelectLocally(player))
        {
            int localVote = await AncientBanSelectionScreen.ShowAndWait(pool, nextActIndex);

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                player,
                choiceId,
                PlayerChoiceResult.FromIndex(localVote));

            return localVote;
        }

        return (await RunManager.Instance.PlayerChoiceSynchronizer
            .WaitForRemoteChoice(player, choiceId))
            .AsIndex();
    }

    private static bool ShouldSelectLocally(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            return RunManager.Instance.NetService.Type != NetGameType.Replay;
        }

        return false;
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
        int chosenIndex = rng.NextInt(remaining.Count);
        return remaining[chosenIndex];
    }

    private static int ResolveBannedIndex(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    {
        List<int> validVotes = votesInPlayerSlotOrder
            .Where(i => i >= 0)
            .ToList();

        if (validVotes.Count == 0)
        {
            return 0;
        }

        Dictionary<int, int> counts = new();
        foreach (int vote in validVotes)
        {
            counts[vote] = counts.GetValueOrDefault(vote, 0) + 1;
        }

        int highestCount = counts.Values.Max();

        List<int> leaders = counts
            .Where(kvp => kvp.Value == highestCount)
            .Select(kvp => kvp.Key)
            .OrderBy(i => i)
            .ToList();

        if (leaders.Count == 1)
        {
            return leaders[0];
        }

        var rng = AncientBanHelpers.CreateVoteResolutionRng(runState, nextActIndex);
        int nextVote = rng.NextItem(leaders);
        if (nextVote != null)
        {
            return nextVote;
        } else { 
            throw new InvalidOperationException("Failed to resolve tied ancient ban vote.");
            
        }
    }
} 