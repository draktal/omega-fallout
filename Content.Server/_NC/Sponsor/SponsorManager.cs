using System.Diagnostics.CodeAnalysis;
using Content.Shared._NC.Sponsors;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Server._NC.Sponsors;

[UsedImplicitly]
public sealed class SponsorManager : ISharedSponsorManager
{
    public void Initialize() { }
    public Dictionary<NetUserId, SponsorLevel> Sponsors = new();
    public void GetSponsor(NetUserId user, [NotNullWhen(true)] out SponsorLevel level)
    {
        if (!Sponsors.TryGetValue(user, out SponsorLevel sponsor))
        {
            level = SponsorLevel.None;
            return;
        }
        level = sponsor;
    }

    public bool TryGetSponsorColor(SponsorLevel level, [NotNullWhen(true)] out string? color)
    {
        return SponsorData.SponsorColor.TryGetValue(level, out color);
    }

    public bool TryGetSponsorGhost(SponsorLevel level, [NotNullWhen(true)] out string? ghost)
    {
        return SponsorData.SponsorGhost.TryGetValue(level, out ghost);
    }
}
