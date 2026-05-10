using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

public struct ChooseTheAncientStartupStepCompletedMessage : INetMessage, IPacketSerializable
{
    public int syncEpoch;
    public int stepIndex;
    public int totalStepCount;
    public string modifierId;
    public uint nextChoiceId;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(syncEpoch);
        writer.WriteInt(stepIndex);
        writer.WriteInt(totalStepCount);
        writer.WriteString(modifierId ?? string.Empty);
        writer.WriteUInt(nextChoiceId, 4);
    }

    public void Deserialize(PacketReader reader)
    {
        syncEpoch = reader.ReadInt();
        stepIndex = reader.ReadInt();
        totalStepCount = reader.ReadInt();
        modifierId = reader.ReadString();
        nextChoiceId = reader.ReadUInt(4);
    }

    public override string ToString()
    {
        return $"{"ChooseTheAncientStartupStepCompletedMessage"} epoch {syncEpoch} step {stepIndex}/{totalStepCount} modifier {modifierId} nextChoiceId {nextChoiceId}";
    }
}
