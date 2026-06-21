using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ChooseTheAncient.ChooseTheAncientCode.Messages;

public struct ChooseTheAncientStartupStepCompletedMessage : INetMessage, IPacketSerializable
{
    private const string MessageName = nameof(ChooseTheAncientStartupStepCompletedMessage);

    public int syncEpoch;
    public int stepIndex;
    public int totalStepCount;
    public string modifierId;
    public uint nextChoiceId;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public MegaCrit.Sts2.Core.Logging.LogLevel LogLevel => MegaCrit.Sts2.Core.Logging.LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public readonly string ModifierIdOrEmpty => modifierId ?? string.Empty;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(syncEpoch);
        writer.WriteInt(stepIndex);
        writer.WriteInt(totalStepCount);
        writer.WriteString(ModifierIdOrEmpty);
        // Choice ids can exceed 15 during DRAFT/SEALED_DECK bootstrap. The second
        // PacketWriter argument is a bit count, not a byte count; using 4 here
        // truncated 16 to 0 and prevented peers from aligning the bootstrap baseline.
        writer.WriteUInt(nextChoiceId, 32);
    }

    public void Deserialize(PacketReader reader)
    {
        syncEpoch = reader.ReadInt();
        stepIndex = reader.ReadInt();
        totalStepCount = reader.ReadInt();
        modifierId = reader.ReadString();
        nextChoiceId = reader.ReadUInt(32);
    }

    public static ChooseTheAncientStartupStepCompletedMessage Create(
        int syncEpoch,
        int stepIndex,
        int totalStepCount,
        string modifierId,
        uint nextChoiceId)
    {
        return new ChooseTheAncientStartupStepCompletedMessage
        {
            syncEpoch = syncEpoch,
            stepIndex = stepIndex,
            totalStepCount = totalStepCount,
            modifierId = modifierId ?? string.Empty,
            nextChoiceId = nextChoiceId
        };
    }

    public readonly override string ToString()
    {
        return $"{MessageName} epoch {syncEpoch} step {stepIndex}/{totalStepCount} modifier {ModifierIdOrEmpty} nextChoiceId {nextChoiceId}";
    }
}
