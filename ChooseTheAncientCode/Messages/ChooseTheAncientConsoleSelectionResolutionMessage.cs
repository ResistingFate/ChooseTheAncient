using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

public struct ChooseTheAncientConsoleSelectionResolutionMessage
    : INetMessage, IPacketSerializable
{
    public int targetActIndex;
    public bool cancelFlow;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel =>
        MegaCrit.Sts2.Core.Logging.LogLevel.VeryDebug;
    public bool ShouldBuffer => false;

    internal readonly ConsoleSelectionResolution Resolution =>
        cancelFlow
            ? ConsoleSelectionResolution.CancelFlow
            : ConsoleSelectionResolution.SkipBallot;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(targetActIndex);
        writer.WriteBool(cancelFlow);
    }

    public void Deserialize(PacketReader reader)
    {
        targetActIndex = reader.ReadInt();
        cancelFlow = reader.ReadBool();
    }

    internal static ChooseTheAncientConsoleSelectionResolutionMessage Create(
        int targetActIndex,
        ConsoleSelectionResolution resolution)
    {
        return new ChooseTheAncientConsoleSelectionResolutionMessage
        {
            targetActIndex = targetActIndex,
            cancelFlow = resolution == ConsoleSelectionResolution.CancelFlow
        };
    }

    public readonly override string ToString()
    {
        return $"{nameof(ChooseTheAncientConsoleSelectionResolutionMessage)} " +
               $"{Resolution} act {targetActIndex + 1}";
    }
}
