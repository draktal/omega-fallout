using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Shared._NC.Sponsors;

[UsedImplicitly]
public sealed class SharedSponsorManager
{
    public Dictionary<NetUserId, SponsorData> CachedSponsors = new();
    public bool TryGetSponsorData(NetUserId userId, [NotNullWhen(true)] out SponsorData? sponsorData)
    {
        return CachedSponsors.TryGetValue(userId, out sponsorData);
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
