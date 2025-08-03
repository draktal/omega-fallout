using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NC.CCCvars;
using Content.Server._NC.Sponsors;
using Content.Shared._NC.DiscordAuth;
using Content.Shared._NC.Sponsors;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Microsoft.Extensions.Caching.Memory;

namespace Content.Server._NC.Discord;

public sealed partial class DiscordAuthManager : IPostInjectInit
{
    [Dependency] private readonly IServerNetManager _netMgr = default!;
    [Dependency] private readonly IPlayerManager _playerMgr = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SponsorManager _sponsors = default!;

    private ISawmill _sawmill = default!;

    private readonly HttpClient _httpClient = new();

    private bool _enabled = false;
    private string _apiUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _discordGuild = String.Empty;
    private TimeSpan _apiDelay = TimeSpan.FromMilliseconds(1200);
    private DateTimeOffset _lastApiCall = DateTimeOffset.MinValue;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        SizeLimit = 1024,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5)
    });
    private readonly MemoryCacheEntryOptions _cacheOptions = new MemoryCacheEntryOptions
    {
        Size = 1,
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    };
    public event EventHandler<ICommonSession>? PlayerVerified;

    public void PostInject()
    {
        IoCManager.InjectDependencies(this);
    }

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("discordAuth");

        _cfg.OnValueChanged(CCCVars.DiscordAuthEnabled, v => _enabled = v, true);
        _cfg.OnValueChanged(CCCVars.DiscordApiUrl, v => _apiUrl = v, true);
        _cfg.OnValueChanged(CCCVars.ApiKey, v => _apiKey = v, true);
        _cfg.OnValueChanged(CCCVars.DiscordGuildID, v => _discordGuild = v, true);

        _netMgr.RegisterNetMessage<MsgDiscordAuthRequired>();
        _netMgr.RegisterNetMessage<MsgSyncSponsorData>();
        _netMgr.RegisterNetMessage<MsgDiscordAuthCheck>(OnAuthCheck);
        _netMgr.Disconnect += OnDisconnect;

        _playerMgr.PlayerStatusChanged += OnPlayerStatusChanged;

        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs e)
    {
        _sponsors.Sponsors.Remove(e.Channel.UserId);
    }

    private async void OnAuthCheck(MsgDiscordAuthCheck msg)
    {
        var data = await IsVerified(msg.MsgChannel.UserId);
        if (!data.Status)
            return;

        var session = _playerMgr.GetSessionById(msg.MsgChannel.UserId);
        PlayerVerified?.Invoke(this, session);
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected)
            return;

        if (!_enabled)
        {
            PlayerVerified?.Invoke(this, args.Session);
            return;
        }


        var data = await IsVerified(args.Session.UserId);
        if (data.Status && data.UserData is not null)
        {
            PlayerVerified?.Invoke(this, args.Session);
            return;
        }

        var link = await GenerateLink(args.Session.UserId);
        var message = new MsgDiscordAuthRequired
        {
            Link = link ?? "",
            ErrorMessage = data.ErrorMessage ?? "",
        };
        args.Session.Channel.SendMessage(message);
    }

    private async Task<DiscordData> IsVerified(NetUserId userId, CancellationToken cancel = default)
    {
        _sawmill.Debug($"Verifying Discord for {userId}");

        var cacheKey = $"discord_verified_{userId}";
        if (_cache.TryGetValue<DiscordData>(cacheKey, out var cached))
            return cached!;

        try
        {
            // 1. Check Discord UUID
            var uuid = await SafeApiRequest(async () =>
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/uuid?method=uid&id={userId}", cancel);
                return response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<DiscordUuidResponse>(cancellationToken: cancel)
                    : null;
            }, "uuid");

            if (uuid == null)
                return UnauthorizedErrorData();

            // 2. Check guild membership
            var inGuild = await CheckGuild(userId, cancel);
            if (!inGuild)
                return NotInGuildErrorData();

            // 3. Get roles
            var roles = await GetRoles(userId, cancel);
            if (roles == null)
                return EmptyResponseErrorRoleData();

            // Process sponsor data
            var level = SponsorData.ParseRoles(roles);
            if (level != SponsorLevel.None)
            {
                _sponsors.Sponsors[userId] = level;
                var message = new MsgSyncSponsorData { UserId = userId, Level = level };
                if (_playerMgr.TryGetSessionById(userId, out var session))
                    _netMgr.ServerSendMessage(message, session.Channel);

                _sawmill.Info($"Sponsor updated: {userId} ({level})");
            }

            var result = new DiscordData(true, new DiscordUserData(userId, uuid.DiscordId));
            _cache.Set(cacheKey, result, _cacheOptions);
            return result;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Discord verification failed: {ex.Message}");
            return UnexpectedErrorData();
        }
    }

    private async Task<bool> CheckGuild(NetUserId userId, CancellationToken cancel = default)
    {
        var cacheKey = $"discord_guild_{userId}";
        if (_cache.TryGetValue<bool>(cacheKey, out var cached))
            return cached;

        return await SafeApiRequest(async () =>
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/guilds?method=uid&id={userId}", cancel);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                await Task.Delay(retryAfter, cancel);
                return await CheckGuild(userId, cancel);
            }

            if (!response.IsSuccessStatusCode)
                return false;

            var guilds = await response.Content.ReadFromJsonAsync<DiscordGuildsResponse>(cancellationToken: cancel);
            var result = guilds?.Guilds.Any(g => g.Id == _discordGuild) ?? false;
            _cache.Set(cacheKey, result, _cacheOptions);
            return result;
        }, "guilds");
    }

    private async Task<List<string>?> GetRoles(NetUserId userId, CancellationToken cancel = default)
    {
        var cacheKey = $"discord_roles_{userId}";
        if (_cache.TryGetValue<List<string>>(cacheKey, out var cached))
            return cached;

        return await SafeApiRequest(async () =>
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/roles?method=uid&id={userId}&guildId={_discordGuild}", cancel);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                await Task.Delay(retryAfter, cancel);
                return await GetRoles(userId, cancel);
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var roles = await response.Content.ReadFromJsonAsync<RolesResponse>(cancellationToken: cancel);
            var result = roles?.Roles.ToList();
            if (result != null)
                _cache.Set(cacheKey, result, _cacheOptions);

            return result;
        }, "roles");
    }

    public async Task<string?> GenerateLink(NetUserId userId, CancellationToken cancel = default)
    {
        _sawmill.Debug($"Generating link for {userId}");

        return await SafeApiRequest(async () =>
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/link?uid={userId}", cancel);
            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Warning($"Failed to generate link for {userId}. Status: {response.StatusCode}");
                return null;
            }

            var link = await response.Content.ReadFromJsonAsync<DiscordLinkResponse>(cancel);
            return link?.Link;
        }, "generate_link");
    }
    private async Task<T> SafeApiRequest<T>(Func<Task<T>> requestFunc, string endpoint)
    {
        var delay = _lastApiCall + _apiDelay - DateTimeOffset.Now;
        if (delay > TimeSpan.Zero)
        {
            _sawmill.Debug($"Delaying API request to {endpoint} by {delay.TotalMilliseconds}ms");
            await Task.Delay(delay);
        }

        try
        {
            _lastApiCall = DateTimeOffset.Now;
            return await requestFunc();
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("rate limited"))
        {
            _sawmill.Warning($"Hit rate limit on {endpoint}, retrying after delay...");
            await Task.Delay(_apiDelay * 2);
            return await SafeApiRequest(requestFunc, endpoint);
        }
    }
}
