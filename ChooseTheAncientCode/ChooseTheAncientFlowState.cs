namespace ChooseTheAncient.ChooseTheAncientCode;

public sealed class ChooseTheAncientFlowState
{
    public HashSet<int> ResolvedActs { get; } = new();
    public bool FlowInProgress { get; set; }
    public bool ContinueEnterNextAct { get; set; }
}