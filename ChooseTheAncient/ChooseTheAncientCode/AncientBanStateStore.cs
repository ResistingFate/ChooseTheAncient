using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode;

public static class AncientBanStateStore
{
    private static readonly ConditionalWeakTable<RunState, AncientBanFlowState> States = new();

    public static AncientBanFlowState Get(RunState runState) => States.GetOrCreateValue(runState);
}