using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

public struct ChooseTheAncientNeowOptionIdentityChosenMessage : INetMessage, IRunLocationTargetedMessage
{
    private const string MessageName = nameof(ChooseTheAncientNeowOptionIdentityChosenMessage);

    public string eventId;
    public string optionIdentity;
    public uint optionIndex;
    public RunLocation location;

    public readonly string EventIdOrEmpty => eventId ?? string.Empty;
    public readonly string OptionIdentityOrEmpty => optionIdentity ?? string.Empty;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.VeryDebug;
    public bool ShouldBuffer => true;
    public RunLocation Location => location;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(EventIdOrEmpty);
        writer.WriteString(OptionIdentityOrEmpty);
        writer.WriteUInt(optionIndex, 32);
        writer.Write(location);
    }

    public void Deserialize(PacketReader reader)
    {
        eventId = reader.ReadString();
        optionIdentity = reader.ReadString();
        optionIndex = reader.ReadUInt(32);
        location = reader.Read<RunLocation>();
    }

    public static ChooseTheAncientNeowOptionIdentityChosenMessage Create(
        string eventId,
        string optionIdentity,
        uint optionIndex,
        RunLocation location)
    {
        return new ChooseTheAncientNeowOptionIdentityChosenMessage
        {
            eventId = eventId ?? string.Empty,
            optionIdentity = optionIdentity ?? string.Empty,
            optionIndex = optionIndex,
            location = location
        };
    }

    public readonly override string ToString()
    {
        return $"{MessageName} event {EventIdOrEmpty} identity {OptionIdentityOrEmpty} index {optionIndex}";
    }
}
