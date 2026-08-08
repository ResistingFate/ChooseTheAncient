using System.Collections.Generic;

namespace ChooseTheAncient.ChooseTheAncientCode;

internal enum ConsoleSelectionResolution
{
    SkipBallot,
    CancelFlow
}

internal readonly record struct ConsoleSelectionResolutionRequest(
    int TargetActIndex,
    ConsoleSelectionResolution Resolution);

public sealed class ChooseTheAncientFlowState
{
    public HashSet<int> ResolvedActs { get; } = new();
    public bool FlowInProgress { get; set; }
    public bool ContinueEnterNextAct { get; set; }
    public bool ModifierBootstrapCompleted { get; set; }
    public bool ForceNeowBlessingMode { get; set; }
    private ConsoleSelectionResolutionRequest? _consoleSelectionResolutionRequest;

    public int? ActiveFlowTargetActIndex { get; set; }
    public bool ConsoleNavigationInProgress { get; set; }
    public bool ConsoleMapSelectionRebasePending { get; set; }
    public HashSet<int> StartingRoomFlowTriggeredActs { get; } = new();
    public int? PendingVanillaMapRoomReplacementActIndex { get; set; }
    private bool SuppressNextAct1StartingRoomFlow { get; set; }

    public bool ForceAct1NeowBlessingMode
    {
        get => ForceNeowBlessingMode;
        set => ForceNeowBlessingMode = value;
    }

    public bool Act1StartingRoomFlowTriggered { get; set; }

    internal void RequestConsoleSelectionResolution(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        _consoleSelectionResolutionRequest =
            new ConsoleSelectionResolutionRequest(targetActIndex, resolution);
    }

    internal bool IsConsoleSelectionResolutionRequestedFor(int targetActIndex)
    {
        return _consoleSelectionResolutionRequest?.TargetActIndex == targetActIndex;
    }

    internal bool IsConsoleSelectionResolutionRequestedFor(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        return _consoleSelectionResolutionRequest is
        {
            TargetActIndex: var requestedTargetActIndex,
            Resolution: var requestedResolution
        }
        && requestedTargetActIndex == targetActIndex
        && requestedResolution == resolution;
    }

    internal bool ConsumeConsoleSelectionResolution(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        if (_consoleSelectionResolutionRequest is not
            {
                TargetActIndex: var requestedTargetActIndex,
                Resolution: var requestedResolution
            }
            || requestedTargetActIndex != targetActIndex
            || requestedResolution != resolution)
        {
            return false;
        }

        _consoleSelectionResolutionRequest = null;
        return true;
    }

    internal void ClearConsoleSelectionResolution()
    {
        _consoleSelectionResolutionRequest = null;
    }

    public void RequestSuppressNextAct1StartingRoomFlow()
    {
        SuppressNextAct1StartingRoomFlow = true;
    }

    public bool ConsumeSuppressNextAct1StartingRoomFlow()
    {
        bool suppress = SuppressNextAct1StartingRoomFlow;
        SuppressNextAct1StartingRoomFlow = false;
        return suppress;
    }

    public void ClearSuppressNextAct1StartingRoomFlow()
    {
        SuppressNextAct1StartingRoomFlow = false;
    }


}
