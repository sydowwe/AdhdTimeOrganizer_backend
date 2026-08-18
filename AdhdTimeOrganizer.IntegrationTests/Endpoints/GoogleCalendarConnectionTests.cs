using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.infrastructure.extService.googleCalendar;
using AdhdTimeOrganizer.infrastructure.persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
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

    private record GoogleCalendarAuthUrlResponse(string Url);

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

    private record StatusResponse(bool Connected);

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
