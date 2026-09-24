using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.extService.user.auth;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

/// <summary>
/// B28 — a revoked session's access token stops authenticating at its <b>next request</b>, not when it
/// expires. Every path that revokes a session is driven through its real endpoint, and each test then probes
/// the access token the dead session is still holding.
/// <para>
/// This is the only guard for <c>SessionRevocationBearerEvents</c>: take the <c>OnTokenValidated</c> line out
/// of <c>IdentityServiceExtensions</c> and nothing else fails — tokens simply keep working after a revoke. It
/// asserts on status codes a stolen token would actually get, not on rows.
/// </para>
/// <para>
/// Every session lives on a raw client (<see cref="AuthTestBase.CreateRawClient"/>): a cookie container would
/// drop the cookies a logout clears, which is exactly the courtesy a stolen copy of the session skips. Each
/// test signs in a fresh account, because revoking the shared fixture user's sessions would reach other tests.
/// Every "before" probe matters too: it makes the session's live verdict cached, so the revoke has to override
/// a cache entry rather than land on an empty one.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SessionRevocationTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = AppDbContextFixture.TestUserPassword;
    private const string ProbePath = "timer-preset";

    // ── One session ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeSession_KillsThatSessionsAccessToken_NotTheRevokersOrAThirdSession()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-one", Password);
        using var client = CreateRawClient();
        var revoker = await SignInAsync(client, email);
        var target = await SignInAsync(client, email);
        var bystander = await SignInAsync(client, email);
        await AllLiveAsync(revoker, target, bystander);

        (await revoker.SendAsync(HttpMethod.Delete, $"user/sessions/{target.SessionId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var probe = await target.SendAsync(HttpMethod.Get, ProbePath);
        probe.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the revoked session's access token is unexpired and correctly signed, and must still be refused");
        probe.Headers.Contains("X-Token-Expired").Should().BeFalse(
            "the token has not expired, so the header that says it has must not be set");

        (await revoker.ProbeAsync()).Should().Be(HttpStatusCode.OK, "revoking another session leaves the revoker's alone");
        (await bystander.ProbeAsync()).Should().Be(HttpStatusCode.OK, "revoking one session leaves a third one alone");
    }

    /// <summary>
    /// What the client really does next: its first 401 triggers one refresh. That refresh must be refused,
    /// and it must not cost anyone else their session. It carries a token revoked <i>without</i> a replacement
    /// link, which <c>ValidateRefreshTokenAsync</c> used to read as the reuse-attack signature. It then revoked
    /// every session of the account, so revoking a device signed the revoker out as well (before B28 at the
    /// revoker's next refresh, after B28 at its next request). This was pinned as a <c>KnownGap</c> until the
    /// deliberately-revoked case was split from the rotated one.
    /// </summary>
    [Fact]
    public async Task RevokeSession_ThenTheRevokedDeviceRefreshes_IsRefusedWithoutSigningOutTheRevoker()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-cascade", Password);
        using var client = CreateRawClient();
        var revoker = await SignInAsync(client, email);
        var target = await SignInAsync(client, email);
        await AllLiveAsync(revoker, target);

        (await revoker.SendAsync(HttpMethod.Delete, $"user/sessions/{target.SessionId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await target.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized);

        (await target.SendAsync(HttpMethod.Post, "auth/refresh")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the revoked device cannot extend its session");
        (await target.SendAsync(HttpMethod.Post, "auth/refresh")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "nor on a retry");

        (await revoker.ProbeAsync()).Should().Be(HttpStatusCode.OK,
            "a refused refresh by the device that was revoked is not evidence of theft");
        await revoker.RefreshAsync();
        (await revoker.ProbeAsync()).Should().Be(HttpStatusCode.OK, "and the revoker's own refresh chain is intact");
    }

    /// <summary>The same for "log out of all other devices": each of them refreshes once, and the caller survives all of it.</summary>
    [Fact]
    public async Task RevokeAllOtherSessions_ThenEachRevokedDeviceRefreshes_TheCallerStaysSignedIn()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-others-cascade", Password);
        using var client = CreateRawClient();
        var caller = await SignInAsync(client, email);
        var other1 = await SignInAsync(client, email);
        var other2 = await SignInAsync(client, email);
        await AllLiveAsync(caller, other1, other2);

        (await caller.SendAsync(HttpMethod.Delete, "user/sessions/all")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var other in new[] { other1, other2 })
            (await other.SendAsync(HttpMethod.Post, "auth/refresh")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await caller.ProbeAsync()).Should().Be(HttpStatusCode.OK);
        await caller.RefreshAsync();
        (await caller.ProbeAsync()).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The session id has to survive rotation, or a client holding the id from the list could never revoke
    /// the tokens minted after its next refresh. Refreshes twice, then revokes by the id the list shows.
    /// </summary>
    [Fact]
    public async Task SessionId_SurvivesRotation_AndRevokingTheListedIdKillsTheLatestAccessToken()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-rotated", Password);
        using var client = CreateRawClient();
        var viewer = await SignInAsync(client, email);
        var target = await SignInAsync(client, email);
        var sessionId = target.SessionId;

        var listedBefore = await ListSessionsAsync(viewer);
        listedBefore.Should().ContainSingle(s => s.Id == sessionId && !s.IsCurrent,
            "the list names the session by the same id its access token carries");

        await target.RefreshAsync();
        await target.RefreshAsync();
        target.SessionId.Should().Be(sessionId, "every access token of the session carries the same sid");

        var listedAfter = await ListSessionsAsync(viewer);
        listedAfter.Select(s => s.Id).Should().BeEquivalentTo(listedBefore.Select(s => s.Id),
            "refreshing must not change the ids the list shows — the client keys its rows and its Revoke call on them");
        listedAfter.Should().ContainSingle(s => s.IsCurrent, "the viewer still recognises its own session");

        (await target.ProbeAsync()).Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(HttpMethod.Delete, $"user/sessions/{sessionId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await target.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized,
            "the access token minted by the latest refresh belongs to the revoked session");
        (await viewer.ProbeAsync()).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeSession_OwnCurrentSession_IsStillRefusedWith400AndStaysLive()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-self", Password);
        using var client = CreateRawClient();
        var caller = await SignInAsync(client, email);
        await caller.RefreshAsync();

        (await caller.SendAsync(HttpMethod.Delete, $"user/sessions/{caller.SessionId}")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the caller's own session is revoked through logout, not here");
        (await caller.ProbeAsync()).Should().Be(HttpStatusCode.OK);
    }

    // ── Every session but one ─────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAllOtherSessions_KillsEveryOtherSessionsAccessToken_NotTheCallers()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-others", Password);
        using var client = CreateRawClient();
        var caller = await SignInAsync(client, email);
        var other1 = await SignInAsync(client, email);
        var other2 = await SignInAsync(client, email);
        await AllLiveAsync(caller, other1, other2);

        (await caller.SendAsync(HttpMethod.Delete, "user/sessions/all")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await other1.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized);
        (await other2.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized);
        (await caller.ProbeAsync()).Should().Be(HttpStatusCode.OK, "the caller's own session is the one that survives");
    }

    // ── The caller's session ──────────────────────────────────────────────────

    /// <summary>
    /// Logout clears the caller's cookies, so this only matters for a copy of the session held elsewhere —
    /// which is the case that matters.
    /// </summary>
    [Fact]
    public async Task Logout_KillsACopiedAccessTokenOfThatSession_NotAnotherSession()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-logout", Password);
        using var client = CreateRawClient();
        var session = await SignInAsync(client, email);
        var copy = session.Copy();
        var other = await SignInAsync(client, email);
        await AllLiveAsync(session, other);

        (await session.SendAsync(HttpMethod.Post, "auth/logout")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await copy.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized);
        (await other.ProbeAsync()).Should().Be(HttpStatusCode.OK);
    }

    // ── Every session ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAll_KillsEverySessionsAccessToken_TheCallersIncluded()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-logout-all", Password);
        using var client = CreateRawClient();
        var caller = await SignInAsync(client, email);
        var other = await SignInAsync(client, email);
        await AllLiveAsync(caller, other);

        (await caller.SendAsync(HttpMethod.Post, "auth/logout-all")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AllDeadAsync(caller, other);
    }

    /// <summary>The endpoint already signed the caller out (it clears the cookies); now a copy of the token is dead too.</summary>
    [Fact]
    public async Task ChangePassword_KillsEverySessionsAccessToken_TheCallersIncluded()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-pwchange", Password);
        using var client = CreateRawClient();
        var caller = await SignInAsync(client, email);
        var other = await SignInAsync(client, email);
        await AllLiveAsync(caller, other);

        (await caller.SendAsync(HttpMethod.Patch, "user/password", new { Password, NewPassword = "NewTest@5678!" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AllDeadAsync(caller, other);
    }

    /// <summary>Anonymous, so there is no caller session to spare: every session dies.</summary>
    [Fact]
    public async Task ResetPassword_KillsEverySessionsAccessToken()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "revoke-pwreset", Password);
        using var client = CreateRawClient();
        var first = await SignInAsync(client, email);
        var second = await SignInAsync(client, email);
        await AllLiveAsync(first, second);

        string token;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GeneratePasswordResetTokenAsync((await userManager.FindByIdAsync(userId.ToString()))!);
        }

        (await client.PostAsJsonAsync("auth/reset-password",
                new { UserId = userId, Token = token, NewPassword = "NewTest@5678!", RecaptchaToken = "test" }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AllDeadAsync(first, second);
    }

    /// <summary>Anonymous — the link is opened from the new mailbox — so every session dies.</summary>
    [Fact]
    public async Task ConfirmEmailChange_KillsEverySessionsAccessToken()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "revoke-emailchange", Password);
        using var client = CreateRawClient();
        var first = await SignInAsync(client, email);
        var second = await SignInAsync(client, email);
        await AllLiveAsync(first, second);

        var newEmail = $"revoke-emailchange-new-{Guid.NewGuid():N}@integration.com";
        string token;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GenerateChangeEmailTokenAsync((await userManager.FindByIdAsync(userId.ToString()))!, newEmail);
        }

        (await client.PostAsJsonAsync("user/change-email/confirm",
                new { UserId = userId, NewEmail = newEmail, Token = token }, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AllDeadAsync(first, second);
    }

    /// <summary>
    /// A rotated refresh token replayed outside the 5-second grace window is the reuse-attack signature, and
    /// the response is to kill every session — the access tokens now included. The rotated row's
    /// <c>RevokedAt</c> is pushed back past the window rather than waiting it out.
    /// </summary>
    [Fact]
    public async Task RefreshTokenReuse_KillsEverySessionsAccessToken()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-reuse", Password);
        using var client = CreateRawClient();
        var victim = await SignInAsync(client, email);
        var other = await SignInAsync(client, email);
        var stolenRefreshToken = victim.RefreshToken;

        await victim.RefreshAsync();
        await AllLiveAsync(victim, other);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            // The cookie carries the token URL-encoded (base64's + / = are escaped); the row is keyed on the raw one.
            var stolenHash = AuthCookies.DeriveSessionHash(Uri.UnescapeDataString(stolenRefreshToken));
            (await db.RefreshTokens.IgnoreQueryFilters()
                    .Where(r => r.TokenHash == stolenHash)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow.AddMinutes(-1)), CancellationToken))
                .Should().Be(1);
        }

        using var replay = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
        replay.Headers.Add("Cookie", $"{AuthCookies.RefreshTokenName}={stolenRefreshToken}");
        (await client.SendAsync(replay, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await AllDeadAsync(victim, other);
    }

    // ── What the check does not do ────────────────────────────────────────────

    /// <summary>
    /// Tokens minted before deploy carry no <c>sid</c>. They are accepted until they expire (at most one
    /// access-token lifetime) rather than signing every user out at deploy.
    /// </summary>
    [Fact]
    public async Task AccessTokenWithoutSessionId_StillAuthenticatesUntilItExpires()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "revoke-legacy", Password);

        string legacyToken;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var token = new JwtSecurityToken(
                issuer: Helper.GetEnvVar("JWT_ISSUER"),
                audience: Helper.GetEnvVar("JWT_AUDIENCE"),
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                    new Claim(ClaimTypes.Role, PortalRoleCatalog.User)
                ],
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: scope.ServiceProvider.GetRequiredService<IEcdsaKeyProvider>().GetSigningCredentials());
            legacyToken = new JwtSecurityTokenHandler().WriteToken(token);
        }

        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbePath);
        request.Headers.Add("Cookie", $"{AuthCookies.AuthTokenName}={legacyToken}");

        (await client.SendAsync(request, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a token from before session ids existed has no session to be revoked, and must not be treated as revoked");
    }

    /// <summary>
    /// The per-process cache is only a shortcut: with its entries gone (a restart, an eviction, another
    /// instance) the check falls back to the refresh-token table, which still says revoked. A silent pass here
    /// is the one failure the design must not have. A live session survives the same eviction.
    /// </summary>
    [Fact]
    public async Task RevokedSession_StaysRejected_WhenTheCacheHasForgottenIt()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-cold", Password);
        using var client = CreateRawClient();
        var revoker = await SignInAsync(client, email);
        var target = await SignInAsync(client, email);
        await AllLiveAsync(revoker, target);

        (await revoker.SendAsync(HttpMethod.Delete, $"user/sessions/{target.SessionId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await target.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized);

        var cache = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IDistributedCache>();
        foreach (var sessionId in new[] { target.SessionId, revoker.SessionId })
        {
            await cache.RemoveAsync(SessionRevocationService.RevokedKey(sessionId), CancellationToken);
            await cache.RemoveAsync(SessionRevocationService.LiveKey(sessionId), CancellationToken);
        }

        (await target.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized,
            "with nothing cached, the database is asked, and it still says the session is revoked");
        (await cache.GetStringAsync(SessionRevocationService.RevokedKey(target.SessionId), CancellationToken))
            .Should().NotBeNull("the database's answer is cached again, so the next request is a cache hit");
        (await revoker.ProbeAsync()).Should().Be(HttpStatusCode.OK, "a live session survives the same eviction");
    }

    // ── Other clients ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extension tokens are minted separately (<c>GenerateTokensForExtensionAsync</c>) and sent as
    /// <c>Authorization: Bearer</c>, so this pins both halves: they carry the sid of their own refresh row, and
    /// the header path gets the same check. The probe is an extension-denied route: 403 proves the token
    /// authenticated, 401 that it no longer does.
    /// </summary>
    [Fact]
    public async Task ExtensionTokens_CarryTheirSessionId_AndAreRefusedOnceItIsRevoked()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "revoke-extension", Password);

        string accessToken, refreshToken;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());
            (accessToken, refreshToken) = await scope.ServiceProvider.GetRequiredService<IJwtService<User>>()
                .GenerateTokensForExtensionAsync(AuthMethodEnum.Password, user!);
        }

        var sessionId = SessionIdOf(accessToken);
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var refreshHash = AuthCookies.DeriveSessionHash(refreshToken);
            var row = await db.RefreshTokens.IgnoreQueryFilters().SingleAsync(r => r.TokenHash == refreshHash, CancellationToken);
            row.SessionId.Should().Be(sessionId);
            row.IsExtensionClient.Should().BeTrue();
        }

        using var client = CreateRawClient();
        (await BearerGetAsync(client, accessToken)).Should().Be(HttpStatusCode.Forbidden,
            "authenticated, and denied only because extension clients may not list sessions");

        // What the extension logout endpoint does.
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().RevokeRefreshTokenAsync(refreshToken);

        (await BearerGetAsync(client, accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpStatusCode> BearerGetAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "user/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(request, CancellationToken)).StatusCode;
    }

    /// <summary>
    /// A hub connection authenticates once, at handshake, so the per-request check never sees it again. The
    /// revoke closes it instead — notifications carry user data. Another session's connection stays open.
    /// Driven over <c>TestServer</c>'s in-memory WebSocket, as in <c>NotificationHubRoundTripTests</c>, but
    /// against the real JwtBearer scheme with the session's cookie on the handshake.
    /// </summary>
    [Fact]
    public async Task RevokeSession_ClosesThatSessionsOpenNotificationHubConnection_NotAnotherSessions()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "revoke-hub", Password);
        using var client = CreateRawClient();
        var revoker = await SignInAsync(client, email);
        var target = await SignInAsync(client, email);

        await using var targetConnection = await ConnectToHubAsync(target);
        await using var revokerConnection = await ConnectToHubAsync(revoker);

        var targetClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        targetConnection.Closed += _ =>
        {
            targetClosed.TrySetResult();
            return Task.CompletedTask;
        };

        (await revoker.SendAsync(HttpMethod.Delete, $"user/sessions/{target.SessionId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await targetClosed.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        targetConnection.State.Should().Be(HubConnectionState.Disconnected);
        revokerConnection.State.Should().Be(HubConnectionState.Connected,
            "only the revoked session's connections are closed");
    }

    private async Task<HubConnection> ConnectToHubAsync(Session session)
    {
        // The same host the sessions signed in on and the revoke goes to: the connection registry and the
        // in-memory cache are per host, so a second factory would hold a connection no revoke here can reach.
        var server = ((WebApplicationFactory<Program>)Fixture.UnauthenticatedFactory).Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, ct) =>
                {
                    var handshakeUri = new UriBuilder(context.Uri) { Scheme = Uri.UriSchemeHttp }.Uri;
                    var webSocketClient = server.CreateWebSocketClient();
                    // How the SPA authenticates the hub: the httpOnly auth cookie rides on the handshake.
                    webSocketClient.ConfigureRequest = request => request.Headers["Cookie"] = session.CookieHeader;
                    return await webSocketClient.ConnectAsync(handshakeUri, ct);
                };
            })
            .Build();

        await connection.StartAsync(CancellationToken);
        connection.State.Should().Be(HubConnectionState.Connected);
        return connection;
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private static async Task AllLiveAsync(params Session[] sessions)
    {
        foreach (var session in sessions)
            (await session.ProbeAsync()).Should().Be(HttpStatusCode.OK, "every session is live before the revoke");
    }

    private static async Task AllDeadAsync(params Session[] sessions)
    {
        foreach (var session in sessions)
            (await session.ProbeAsync()).Should().Be(HttpStatusCode.Unauthorized,
                "every session was revoked, so none of their unexpired access tokens may authenticate");
    }

    private static async Task<List<UserSessionResponse>> ListSessionsAsync(Session session)
    {
        var response = await session.SendAsync(HttpMethod.Get, "user/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<UserSessionResponse>>(CancellationToken))!;
    }

    private static async Task<Session> SignInAsync(HttpClient client, string email)
    {
        var payload = new { Email = email, Password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();

        var session = new Session(client);
        session.Absorb(response);
        return session;
    }

    private static Guid SessionIdOf(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .Single(c => c.Type == AuthClaims.SessionId).Value);

    /// <summary>One signed-in device: the three session cookies, replayed by hand on every request.</summary>
    private sealed class Session(HttpClient client)
    {
        private readonly Dictionary<string, string> cookies = new();

        public string RefreshToken => cookies[AuthCookies.RefreshTokenName];
        public Guid SessionId => SessionIdOf(cookies[AuthCookies.AuthTokenName]);
        public string CookieHeader => string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));

        /// <summary>A second holder of the same cookies — what a stolen session looks like.</summary>
        public Session Copy()
        {
            var copy = new Session(client);
            foreach (var (name, value) in cookies)
                copy.cookies[name] = value;
            return copy;
        }

        /// <summary>Takes the cookies a login or refresh issued. Deletions (empty values) are ignored on purpose.</summary>
        public void Absorb(HttpResponseMessage response)
        {
            foreach (var setCookie in response.Headers.GetValues("Set-Cookie"))
            {
                var (name, value) = setCookie.Split(';', 2)[0].Split('=', 2) switch
                {
                    [var n, var v] => (n, v),
                    _ => (setCookie, "")
                };
                if (value.Length > 0)
                    cookies[name] = value;
            }

            cookies.Should().ContainKeys(AuthCookies.AuthTokenName, AuthCookies.RefreshTokenName, AuthCookies.SessionHashName);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Cookie", CookieHeader);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, CancellationToken);
        }

        public async Task<HttpStatusCode> ProbeAsync() => (await SendAsync(HttpMethod.Get, ProbePath)).StatusCode;

        public async Task RefreshAsync()
        {
            var response = await SendAsync(HttpMethod.Post, "auth/refresh");
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            Absorb(response);
        }
    }
}
