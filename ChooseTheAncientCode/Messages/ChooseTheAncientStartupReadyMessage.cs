using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

    public struct ChooseTheAncientStartupReadyMessage : INetMessage, IPacketSerializable
    {
    public int actIndex;
    public int barrierEpoch;
    public uint nextChoiceId;
    public bool bootstrapCompleted;
    public bool shellRoomActive;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(barrierEpoch);
        writer.WriteUInt(nextChoiceId);
        writer.WriteBool(bootstrapCompleted);
        writer.WriteBool(shellRoomActive);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        barrierEpoch = reader.ReadInt();
        nextChoiceId = reader.ReadUInt();
        bootstrapCompleted = reader.ReadBool();
        shellRoomActive = reader.ReadBool();
    }
}
