using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class ChooseTheAncientStateStore
{
    private static readonly ConditionalWeakTable<RunState, ChooseTheAncientFlowState> States = new();

    public static ChooseTheAncientFlowState Get(RunState runState) => States.GetOrCreateValue(runState);
}