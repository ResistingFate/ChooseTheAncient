using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Compatibility;

internal static class MapVoteCompatibility
{
    private static readonly PropertyInfo? LegacyVoteDictionary =
        typeof(NMapScreen).GetProperty("PlayerVoteDictionary");

    /// <summary>
    /// clearsVotes for v0.111.0 separately as it got consolidated into the map synchronzier in v0.113.0
    /// </summary>
    public static void ResetVotes(NMapScreen mapScreen, MapLocation location)
    {
        RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(location);

        if (LegacyVoteDictionary?.GetValue(mapScreen) is IDictionary cachedVotes)
        {
            cachedVotes.Clear();
        }

        mapScreen.RefreshAllMapPointVotes();
    }
}
