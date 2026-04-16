using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

public struct ChooseTheAncientNeowOptionIdentityChosenMessage : INetMessage, IRunLocationTargetedMessage
{
    public string eventId;
    public string optionIdentity;
    public uint optionIndex;
    public RunLocation location;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.VeryDebug;
    public RunLocation Location => location;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(eventId ?? string.Empty);
        writer.WriteString(optionIdentity ?? string.Empty);
        writer.WriteUInt(optionIndex, 4);
        writer.Write(location);
    }

    public void Deserialize(PacketReader reader)
    {
        eventId = reader.ReadString();
        optionIdentity = reader.ReadString();
        optionIndex = reader.ReadUInt(4);
        location = reader.Read<RunLocation>();
    }

    public override string ToString()
    {
        return $"{"ChooseTheAncientNeowOptionIdentityChosenMessage"} event {eventId} identity {optionIdentity} index {optionIndex}";
    }
}
