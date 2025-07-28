using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading.Tasks;
using Content.Server._NC.Discord;
using Content.Server._NC.CCCvars;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Content.Shared._NC.Sponsors;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace Content.Server._NC.Sponsors;

public sealed class SponsorsManager : ISponsorsManager
{
    public Dictionary<NetUserId, SponsorData> CachedSponsors = new();

    public void Initialize() { }

    private void OnDisconnect(object? sender, NetDisconnectedArgs e)
    {
        CachedSponsors.Remove(e.Channel.UserId);
    }

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
