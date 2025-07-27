using Robust.Shared.Network;

namespace Content.Shared._NC.Sponsors;

public interface ISponsorsManager
{
    void Initialize();
    bool TryGetSponsorData(NetUserId userId, out SponsorData? sponsorData);
    bool TryGetSponsorColor(SponsorLevel level, out string? color);
    bool TryGetSponsorGhost(SponsorLevel level, out string? ghost);
}
