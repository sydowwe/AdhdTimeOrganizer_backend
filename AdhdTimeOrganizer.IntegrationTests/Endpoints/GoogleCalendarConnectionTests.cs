using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.infrastructure.extService.googleCalendar;
using AdhdTimeOrganizer.infrastructure.persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-19 -- <c>Connect</c>/<c>Disconnect</c>/<c>GetGoogleCalendarAuthUrl</c>/<c>GetGoogleCalendarStatus</c>.
/// A different endpoint family from <c>SyncCalendarToGoogleTests</c> (which covers
/// <c>SyncCalendarToGoogleEndpoint</c>); these four never reach the real Google API except through
/// <c>IGoogleCalendarService.ExchangeCodeForRefreshToken</c>, which every Connect test stubs via Moq.
/// <c>GetAuthUrl</c>/<c>GenerateState</c> are pure local string building, so those tests run against the
/// real <see cref="GoogleCalendarService"/> on the default cached factory -- no mock needed.
/// </summary>
[Collection("Postgres")]
public class GoogleCalendarConnectionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string AuthUrlRoute = "api/user/google-calendar/auth-url";
    private const string ConnectRoute = "api/user/google-calendar/connect";
    private const string DisconnectRoute = "api/user/google-calendar/disconnect";
    private const string StatusRoute = "api/user/google-calendar/status";

    // ── auth gate ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "auth-url requires a logged-in user")]
    public async Task AuthUrl_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(AuthUrlRoute, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "connect requires a logged-in user")]
    public async Task Connect_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(ConnectRoute, new { Code = "c", State = "s" }, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "disconnect requires a logged-in user")]
    public async Task Disconnect_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().DeleteAsync(DisconnectRoute, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "status requires a logged-in user")]
    public async Task Status_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(StatusRoute, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── auth-url ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "auth-url returns a URL that carries no client secret")]
    public async Task AuthUrl_ReturnsUrlWithoutLeakingSecret()
    {
        var response = await CreateClient().GetAsync(AuthUrlRoute, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GoogleCalendarAuthUrlResponse>(JsonOpts, CancellationToken);
        body!.Url.Should().NotBeNullOrEmpty();
        body.Url.Should().Contain("client_id=").And.NotContain("client_secret", "the auth URL is public-facing and must never carry the OAuth client secret");
        body.Url.Should().NotContain(Environment.GetEnvironmentVariable("OAUTH2_GOOGLE_CLIENT_SECRET")!);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue("the state must round-trip via the state cookie");
        cookies!.Should().Contain(c => c.StartsWith($"{GoogleCalendarService.StateCookieName}="));
    }

    /// <summary>
    /// B14 §1 — the fixture sets <c>OAUTH2_GOOGLE_CLIENT_ID</c>, which is the fallback the service uses,
    /// so the cached factories are "configured". Blank both sources through configuration and the
    /// environment to see the unconfigured branch.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] BlankGoogleClient =
    [
        new("GoogleCalendar:ClientId", ""),
        new("GoogleCalendar:ClientSecret", "")
    ];

    private sealed class ScopedEnvVar : IDisposable
    {
        private readonly (string Name, string? Previous)[] _saved;

        public ScopedEnvVar(params string[] names)
        {
            _saved = names.Select(n => (n, Environment.GetEnvironmentVariable(n))).ToArray();
            foreach (var name in names)
                Environment.SetEnvironmentVariable(name, null);
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _saved)
                Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact(DisplayName = "auth-url with no client id is a 503 ServiceNotConfigured, not a 200 carrying client_id=")]
    public async Task AuthUrl_NotConfigured_Returns503_NotABrokenUrl()
    {
        // The env vars are process-wide; the singleton reads them once at construction, so the scope only
        // needs to cover factory creation and the first request that materialises it.
        using var env = new ScopedEnvVar("OAUTH2_GOOGLE_CLIENT_ID", "OAUTH2_GOOGLE_CLIENT_SECRET");
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: BlankGoogleClient);

        var response = await factory.CreateClient().GetAsync(AuthUrlRoute, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "a URL the server knows Google will bounce with 'Missing required parameter: client_id' must not be a 200");
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.ServiceNotConfigured));
        response.Headers.Contains("Set-Cookie").Should().BeFalse("no flow was started, so no state cookie should be minted");
    }

    [Fact(DisplayName = "status says configured=false when no client id is set, and configured=true when one is")]
    public async Task Status_ReportsConfigured()
    {
        using (new ScopedEnvVar("OAUTH2_GOOGLE_CLIENT_ID", "OAUTH2_GOOGLE_CLIENT_SECRET"))
        {
            await using var unconfigured = CreateFactory(TestRoles.AdminAndUser, configOverrides: BlankGoogleClient);
            var status = await unconfigured.CreateClient().GetFromJsonAsync<StatusResponse>(StatusRoute, JsonOpts, CancellationToken);
            status!.Configured.Should().BeFalse("the client hides Connect and words the card as 'not available here'");
            status.Connected.Should().BeFalse();
        }

        var configured = await CreateClient().GetFromJsonAsync<StatusResponse>(StatusRoute, JsonOpts, CancellationToken);
        configured!.Configured.Should().BeTrue("the fixture's OAUTH2_GOOGLE_CLIENT_ID is the sign-in client the calendar flow falls back to");
    }

    [Fact(DisplayName = "auth-url's redirect_uri is PAGE_URL + the SPA callback route, not a config literal")]
    public async Task AuthUrl_RedirectUri_DerivedFromPageUrl()
    {
        var response = await CreateClient().GetAsync(AuthUrlRoute, CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GoogleCalendarAuthUrlResponse>(JsonOpts, CancellationToken);

        var expected = Environment.GetEnvironmentVariable("PAGE_URL")!.TrimEnd('/') + GoogleCalendarService.CallbackPath;
        body!.Url.Should().Contain($"redirect_uri={Uri.EscapeDataString(expected)}",
            "B14 §2: the old literal pointed at http://localhost:5173, a port nothing serves; the SPA lives at PAGE_URL");
    }

    private static async Task<string?> ReadErrorTypeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("errorType", out var errorType) ? errorType.GetString() : null;
    }

    private record GoogleCalendarAuthUrlResponse(string Url);

    // ── state cookie round trip, the way a browser does it ───────────────────

    /// <summary>
    /// B14 §3. Every other connect test writes the <c>Cookie</c> header by hand, which is exactly why the
    /// suite stayed green while every real browser got the "state is missing" 400: the cookie was scoped
    /// to <c>/user/google-calendar</c> and the request goes to <c>/api/user/google-calendar/connect</c>.
    /// A <see cref="CookieContainer"/> applies the same path-prefix rule a browser does, so this is the
    /// one test that actually exercises the scope. HTTPS base address because the cookie is
    /// <c>Secure</c> and the container, like a browser, will not return it over plain http.
    /// </summary>
    [Fact(DisplayName = "the state cookie auth-url sets is sent back on connect by a path-honouring cookie jar")]
    public async Task StateCookie_RoundTripsThroughCookieContainer()
    {
        var mock = StrictCalendarMock();
        mock.SetupGet(s => s.IsConfigured).Returns(true);
        mock.Setup(s => s.GenerateState()).Returns("jar-state");
        mock.Setup(s => s.GetAuthUrl("jar-state")).Returns("https://accounts.google.com/o/oauth2/v2/auth?state=jar-state");
        mock.Setup(s => s.ExchangeCodeForRefreshToken("good-code")).ReturnsAsync("jar-refresh-token");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(mock.Object);
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        var authUrl = await client.GetAsync(AuthUrlRoute, CancellationToken);
        authUrl.StatusCode.Should().Be(HttpStatusCode.OK);
        authUrl.Headers.GetValues("Set-Cookie").Should().ContainSingle(c => c.StartsWith($"{GoogleCalendarService.StateCookieName}="))
            .Which.Should().Contain("path=/api/user/google-calendar", "the scope must include the route prefix the connect request is actually sent under");

        // No Cookie header set by hand: only the jar can supply it, and only if the path matches.
        var connect = await client.PostAsJsonAsync(ConnectRoute, new { Code = "good-code", State = "jar-state" }, CancellationToken);

        connect.StatusCode.Should().Be(HttpStatusCode.NoContent, "the cookie jar must have sent the state cookie back on the prefixed route");
        mock.Verify(s => s.ExchangeCodeForRefreshToken("good-code"), Times.Once);

        await using var db = CreateDbContext();
        var user = await db.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
        user.GoogleCalendarRefreshToken.Should().Be("jar-refresh-token");

        // Connect consumes the state: the same jar cannot replay it.
        var replay = await client.PostAsJsonAsync(ConnectRoute, new { Code = "good-code", State = "jar-state" }, CancellationToken);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest, "connect deletes the cookie on the same path it was set under");
        (await ReadErrorTypeAsync(replay)).Should().Be(nameof(ResultErrorType.CookieMissing));
    }

    // ── connect ──────────────────────────────────────────────────────────────

    private static Mock<IGoogleCalendarService> StrictCalendarMock() => new(MockBehavior.Strict);

    private async Task<HttpResponseMessage> PostConnectAsync(HttpClient client, string code, string state, string? stateCookieValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ConnectRoute)
        {
            Content = JsonContent.Create(new { Code = code, State = state })
        };
        if (stateCookieValue is not null)
            request.Headers.Add("Cookie", $"{GoogleCalendarService.StateCookieName}={stateCookieValue}");
        return await client.SendAsync(request, CancellationToken);
    }

    [Fact(DisplayName = "connect without a matching state cookie is a clean 400, and Google is never called")]
    public async Task Connect_StateMismatch_Returns400_NeverCallsGoogle()
    {
        var mock = StrictCalendarMock();
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(mock.Object);
        });

        var response = await PostConnectAsync(factory.CreateClient(), "some-code", "expected-state", stateCookieValue: "different-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mock.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "connect with no state cookie at all is a clean 400, and Google is never called")]
    public async Task Connect_NoStateCookie_Returns400_NeverCallsGoogle()
    {
        var mock = StrictCalendarMock();
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(mock.Object);
        });

        var response = await PostConnectAsync(factory.CreateClient(), "some-code", "expected-state", stateCookieValue: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mock.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "connect with a valid state but a failed Google exchange is a clean 400 and saves nothing")]
    public async Task Connect_ExchangeFails_Returns400_NoTokenSaved()
    {
        var mock = StrictCalendarMock();
        mock.Setup(s => s.ExchangeCodeForRefreshToken("bad-code")).ReturnsAsync((string?)null);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(mock.Object);
        });

        var response = await PostConnectAsync(factory.CreateClient(), "bad-code", "matching-state", stateCookieValue: "matching-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mock.Verify(s => s.ExchangeCodeForRefreshToken("bad-code"), Times.Once);
        mock.VerifyNoOtherCalls();

        await using var db = CreateDbContext();
        var user = await db.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
        user.GoogleCalendarRefreshToken.Should().BeNull("a failed exchange with Google must not silently connect the account");
    }

    // ── connect/disconnect/status round trip ────────────────────────────────

    [Fact(DisplayName = "connect saves the refresh token and flips status; disconnect clears it and flips status back")]
    public async Task ConnectDisconnectRoundTrip_FlipsReportedStatus()
    {
        var mock = StrictCalendarMock();
        mock.SetupGet(s => s.IsConfigured).Returns(true); // status reports it alongside Connected
        mock.Setup(s => s.ExchangeCodeForRefreshToken("good-code")).ReturnsAsync("real-refresh-token");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(mock.Object);
        });
        var client = factory.CreateClient();

        (await client.GetFromJsonAsync<StatusResponse>(StatusRoute, JsonOpts, CancellationToken))!
            .Connected.Should().BeFalse("the seeded test user starts disconnected");

        var connectResponse = await PostConnectAsync(client, "good-code", "matching-state", stateCookieValue: "matching-state");
        connectResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
            user.GoogleCalendarRefreshToken.Should().Be("real-refresh-token");
        }

        (await client.GetFromJsonAsync<StatusResponse>(StatusRoute, JsonOpts, CancellationToken))!
            .Connected.Should().BeTrue("connect must be immediately reflected by status");

        var disconnectResponse = await client.DeleteAsync(DisconnectRoute, CancellationToken);
        disconnectResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
            user.GoogleCalendarRefreshToken.Should().BeNull();
        }

        (await client.GetFromJsonAsync<StatusResponse>(StatusRoute, JsonOpts, CancellationToken))!
            .Connected.Should().BeFalse("disconnect must be immediately reflected by status");
    }

    private record StatusResponse(bool Configured, bool Connected);

    // ── disconnect / cross-user ──────────────────────────────────────────────

    [Fact(DisplayName = "disconnect only ever clears the caller's own token, never another user's")]
    public async Task Disconnect_OnlyClearsCallersOwnToken()
    {
        await using (var db = CreateDbContext())
        {
            var other = await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            other.GoogleCalendarRefreshToken = "other-users-token";

            var caller = await db.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
            caller.GoogleCalendarRefreshToken = "callers-token";
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateClient().DeleteAsync(DisconnectRoute, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verify = CreateDbContext();
        var caller2 = await verify.Set<User>().SingleAsync(u => u.Id == FakeLoggedUserService.TestUserId, CancellationToken);
        caller2.GoogleCalendarRefreshToken.Should().BeNull();

        var other2 = await verify.Set<User>().SingleAsync(u => u.Id == ReminderSeedHelper.OtherUserId, CancellationToken);
        other2.GoogleCalendarRefreshToken.Should().Be("other-users-token", "disconnect must never touch another user's connection");
    }
}
