using System.Diagnostics.CodeAnalysis;
using Content.Shared._NC.Sponsors;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Client._NC.Sponsors;

[UsedImplicitly]
public sealed class SponsorManager : ISharedSponsorManager
{
    [Dependency] private readonly IClientNetManager _netMgr = default!;
    private SponsorLevel _level = SponsorLevel.None;
    public void Initialize()
    {
        _netMgr.RegisterNetMessage<MsgSyncSponsorData>(OnSponsorDataReceived);
    }

    private void OnSponsorDataReceived(MsgSyncSponsorData message)
    {
        _level = message.Level;
    }

    public void GetSponsor(NetUserId user, out SponsorLevel level)
    {
        level = _level;
    }
}
