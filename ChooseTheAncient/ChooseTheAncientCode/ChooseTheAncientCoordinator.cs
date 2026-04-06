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

public static class ChooseTheAncientCoordinator
{
    public static async Task RunAsync(
        RunManager runManager,
        RunState runState,
        int nextActIndex,
        ChooseTheAncientFlowState flow)
    {
        ChooseTheAncientSelectionScreen? localScreen = null;

        try
        {
            // Handle Host's AncientCount if the client
            List<Player> orderedPlayers = runState.Players
                .OrderBy(runState.GetPlayerSlotIndex)
                .ToList();
            int ancientCount = await GetEffectiveAncientCountAsync(orderedPlayers);

            ActModel nextAct = runState.Acts[nextActIndex];
            List<AncientEventModel> pool = ChooseTheAncientHelpers.BuildCandidatePool(nextAct, runState);
            if (ModLog.IsDebugEnabled)
            {
                string ancientPool = string.Join(",", pool.Select(ancient => ancient.Id.Entry));
                ModLog.Debug($"Available ancients to draw {ancientCount} from: {ancientPool}");
            }
            pool = ChooseTheAncientHelpers.LimitCandidatePoolForVote(runState, nextActIndex, pool, ancientCount);

            ChooseTheAncientHelpers.LogPool($"Act {nextActIndex + 1} initial ballot", pool);

            if (pool.Count <= 1)
            {
                if (pool.Count == 1)
                {
                    ChooseTheAncientHelpers.SetChosenAncient(nextAct, pool[0]);
                    ModLog.Info($"Only one ancient available for act {nextActIndex + 1}: {pool[0].Id.Entry}");
                }

                flow.ResolvedActs.Add(nextActIndex);
                flow.ContinueEnterNextAct = true;
                await runManager.EnterNextAct();
                return;
            }

            // Copied code to get localPlayer from Megacrit
            Player? localPlayer = orderedPlayers.FirstOrDefault(ShouldSelectLocally);
            if (localPlayer != null)
            {
                localScreen = ChooseTheAncientSelectionScreen.Show(nextActIndex, orderedPlayers);
            }

            List<AncientEventModel> finalists = pool;
            List<int> firstVotes = new();

            if (pool.Count >= 2)
            {
                var firstRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                    pool,
                    ChooseTheAncientSelectionScreen.VoteRoundType.InitialKeepVote,
                    null,
                    null,
                    null);

                // firstVotes is a list of voted ancient indices in the original pool
                firstVotes = await CollectVotes(
                    orderedPlayers,
                    firstRound,
                    localScreen);

                int firstPlaceIndex = ResolveMostVotedIndex(
                    runState,
                    nextActIndex,
                    pool.Count,
                    firstVotes);

                // Map reduced-pool indices back to original-pool indices
                List<int> remainingIndices = Enumerable.Range(0, pool.Count)
                    .Where(i => i != firstPlaceIndex)
                    .ToList();

                // Remove votes for the first-place ancient, then reindex the rest
                List<int> secondPlaceVotes = firstVotes
                    .Where(voteIndex => voteIndex != firstPlaceIndex)
                    .Select(voteIndex => voteIndex > firstPlaceIndex ? voteIndex - 1 : voteIndex)
                    .ToList();

                int secondPlaceReducedIndex = ResolveMostVotedIndex(
                    runState,
                    nextActIndex,
                    remainingIndices.Count,
                    secondPlaceVotes);

                int secondPlaceIndex = remainingIndices[secondPlaceReducedIndex];

                AncientEventModel firstAncient = pool[firstPlaceIndex];
                AncientEventModel secondAncient = pool[secondPlaceIndex];

                if (localScreen != null)
                {
                    await localScreen.PlayInitialVoteResolutionAsync(firstVotes, firstPlaceIndex);
                }
                
                // Build finalists directly instead of filtering the whole pool
                finalists = [firstAncient, secondAncient];

                ModLog.Info($"First-pass elimination kept {firstAncient.Id.Entry}, {secondAncient.Id.Entry}.");
                ChooseTheAncientHelpers.LogPool($"Act {nextActIndex + 1} finalists", finalists);
            }

            AncientEventModel chosen;
            if (finalists.Count == 1)
            {
                chosen = finalists[0];
            }
            else
            {
                Dictionary<string, ChooseTheAncientHelpers.AncientPreviewData>? localPreviewData = null;
                if (localPlayer != null)
                {
                    // Builds for each ancient in case I want to allow all ancients to show relics
                    localPreviewData = ChooseTheAncientHelpers.BuildPreviewDataByAncientId(
                        localPlayer,
                        finalists,
                        nextActIndex);
                }

                (string? suppressedPreviewAncientId, string? reactionAncientId) = ResolveSecondRoundPresentation(
                    runState,
                    nextActIndex,
                    pool,
                    finalists,
                    firstVotes);

                var secondRound = new ChooseTheAncientSelectionScreen.RoundDefinition(
                    finalists,
                    ChooseTheAncientSelectionScreen.VoteRoundType.FinalRevealVote,
                    localPreviewData,
                    suppressedPreviewAncientId,
                    reactionAncientId);

                List<int> finalVotes = await CollectVotes(
                    orderedPlayers,
                    secondRound,
                    localScreen);

                int chosenIndex = ResolveMostVotedIndex(
                    runState,
                    nextActIndex,
                    finalists.Count,
                    finalVotes);

                if (localScreen != null)
                {
                    await localScreen.PlayFinalVoteResolutionAsync(finalVotes, chosenIndex);
                }

                chosen = finalists[chosenIndex];
            }

            ChooseTheAncientHelpers.SetChosenAncient(nextAct, chosen);
            ModLog.Info($"Chosen ancient for act {nextActIndex + 1}: {chosen.Id.Entry}");

            flow.ResolvedActs.Add(nextActIndex);
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        catch (Exception ex)
        {
            ModLog.Error($"Ancient selection flow failed: {ex}");
            flow.ContinueEnterNextAct = true;
            await runManager.EnterNextAct();
        }
        finally
        {
            localScreen?.CloseScreen();
            flow.FlowInProgress = false;
        }
    }

    private static async Task<List<int>> CollectVotes(
        IReadOnlyList<Player> orderedPlayers,
        ChooseTheAncientSelectionScreen.RoundDefinition round,
        ChooseTheAncientSelectionScreen? localScreen)
    {
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
                round,
                localScreen))
            .ToArray();

        int[] votes = await Task.WhenAll(voteTasks);

        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            ModLog.Debug($"Vote received for player {orderedPlayers[i].NetId}: {votes[i]}");
        }

        return votes.ToList();
    }

    private static async Task<int> GetVoteForPlayer(
        Player player,
        uint choiceId,
        ChooseTheAncientSelectionScreen.RoundDefinition round,
        ChooseTheAncientSelectionScreen? localScreen)
    {
        if (ShouldSelectLocally(player))
        {
            if (localScreen == null)
            {
                throw new InvalidOperationException("Local ancient selection screen was not created.");
            }

            int localVote = await localScreen.RunRoundAsync(round);

            localScreen.RecordVote(player, localVote);

            // It seems choice synchronizer needs localVote as they are done separately compared to remoteVote
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                player,
                choiceId,
                PlayerChoiceResult.FromIndex(localVote));

            return localVote;
        }

        int remoteVote = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(player, choiceId))
            .AsIndex();

        localScreen?.RecordVote(player, remoteVote);
        return remoteVote;
    }

    private static bool ShouldSelectLocally(Player player)
    {
        if (LocalContext.IsMe(player))
        {
            return RunManager.Instance.NetService.Type != NetGameType.Replay;
        }

        return false;
    }

    private static (string? suppressedPreviewAncientId, string? reactionAncientId) ResolveSecondRoundPresentation(
        RunState runState,
        int nextActIndex,
        IReadOnlyList<AncientEventModel> firstRoundPool,
        IReadOnlyList<AncientEventModel> finalists,
        IReadOnlyList<int> firstVotes)
    {
        if (finalists.Count != 2)
        {
            return (null, null);
        }

        Dictionary<string, int> finalistVoteCounts = finalists
            .ToDictionary(ancient => ancient.Id.Entry, _ => 0);

        foreach (int vote in firstVotes)
        {
            if (vote < 0 || vote >= firstRoundPool.Count)
            {
                continue;
            }

            string votedAncientId = firstRoundPool[vote].Id.Entry;
            if (finalistVoteCounts.ContainsKey(votedAncientId))
            {
                finalistVoteCounts[votedAncientId]++;
            }
        }

        AncientEventModel suppressedPreviewAncient;
        int leftCount = finalistVoteCounts[finalists[0].Id.Entry];
        int rightCount = finalistVoteCounts[finalists[1].Id.Entry];

        if (leftCount == rightCount)
        {
            var rng = ChooseTheAncientHelpers.CreateSecondRoundPresentationRng(runState, nextActIndex);
            suppressedPreviewAncient = finalists[rng.NextInt(finalists.Count)];
        }
        else
        {
            suppressedPreviewAncient = leftCount > rightCount
                ? finalists[0]
                : finalists[1];
        }

        AncientEventModel reactionAncient = finalists
            .First(ancient => ancient.Id.Entry != suppressedPreviewAncient.Id.Entry);

        ModLog.Debug($"Second vote presentation decided from round-one votes: suppress={suppressedPreviewAncient.Id.Entry}, reaction={reactionAncient.Id.Entry}, voteCounts={leftCount}/{rightCount}");

        return (suppressedPreviewAncient.Id.Entry, reactionAncient.Id.Entry);
    }
    
    private static int ResolveMostVotedIndex(
        RunState runState,
        int nextActIndex,
        int optionCount,
        IReadOnlyList<int> votesInPlayerSlotOrder)
    {
        List<int> leaders = ResolveIndicesWithTargetCount(
            optionCount,
            votesInPlayerSlotOrder,
            selectMinimum: false);

        if (leaders.Count == 1)
        {
            return leaders[0];
        }

        var rng = ChooseTheAncientHelpers.CreateFinalVoteResolutionRng(runState, nextActIndex);
        return leaders[rng.NextInt(leaders.Count)];
    }

    private static List<int> ResolveIndicesWithTargetCount(
        int optionCount,
        IReadOnlyList<int> votesInPlayerSlotOrder,
        bool selectMinimum)
    {
        if (optionCount <= 0)
        {
            throw new InvalidOperationException("Cannot resolve a vote for an empty option list.");
        }

        Dictionary<int, int> counts = Enumerable.Range(0, optionCount)
            .ToDictionary(index => index, _ => 0);

        foreach (int vote in votesInPlayerSlotOrder)
        {
            if (vote >= 0 && vote < optionCount)
            {
                counts[vote]++;
            }
        }

        int target = selectMinimum
            ? counts.Values.Min()
            : counts.Values.Max();

        return counts
            .Where(kvp => kvp.Value == target)
            .Select(kvp => kvp.Key)
            .OrderBy(index => index)
            .ToList();
    }
    
    private static Player GetHostPlayer(IReadOnlyList<Player> orderedPlayers)
    {
        switch (RunManager.Instance.NetService.Type)
        {
            case NetGameType.Singleplayer:
            case NetGameType.Replay:
            case NetGameType.Host:
                return LocalContext.GetMe(orderedPlayers) ?? orderedPlayers[0];

            case NetGameType.Client:
                if (RunManager.Instance.NetService is INetClientGameService clientService &&
                    clientService.NetClient != null)
                {
                    ulong hostNetId = clientService.NetClient.HostNetId;
                    Player? hostPlayer = orderedPlayers.FirstOrDefault(p => p.NetId == hostNetId);
                    if (hostPlayer != null)
                        return hostPlayer;
                }

                break;
        }

        return orderedPlayers[0];
    }

    private static async Task<int> GetEffectiveAncientCountAsync(IReadOnlyList<Player> orderedPlayers)
    {
        ChooseTheAncientConfig.RefreshFromModConfig();

        if (RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
        {
            return ChooseTheAncientConfig.AncientCount;
        }

        Player hostPlayer = GetHostPlayer(orderedPlayers);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(hostPlayer);

        if (LocalContext.IsMe(hostPlayer))
        {
            int hostAncientCount = ChooseTheAncientConfig.AncientCount;

            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                hostPlayer,
                choiceId,
                PlayerChoiceResult.FromIndex(hostAncientCount));

            ModLog.Debug($"Broadcasting host AncientCount={hostAncientCount}");
            return hostAncientCount;
        }

        int syncedCount = (await RunManager.Instance.PlayerChoiceSynchronizer
                .WaitForRemoteChoice(hostPlayer, choiceId))
            .AsIndex();

        syncedCount = Math.Clamp(syncedCount, 2, 8);

        ModLog.Debug($"Received host AncientCount={syncedCount}");
        return syncedCount;
    }
}
