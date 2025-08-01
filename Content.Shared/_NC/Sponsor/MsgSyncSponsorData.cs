using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Sponsors;

public sealed class MsgSyncSponsorData : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public SponsorLevel Level;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Level = (SponsorLevel) buffer.ReadByte();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Level);
    }
}
