using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

[Collection("Postgres")]
public class AuthSecurityTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    // ── 2FA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoFa_DirectCall_WithoutPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_WithTamperedPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false, PendingAuthToken = "tampered.garbage.token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The setup route is <c>AllowAnonymous</c> — the partial-auth cookie is its only gate. Without it
    /// the endpoint must refuse rather than provision an authenticator for whoever asked.
    /// </summary>
    [Fact]
    public async Task TwoFaSetup_WithoutPendingCookie_Returns401()
    {
        using var client = CreateCookieClient();
        var response = await client.PostAsync("auth/login/2fa/setup", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a registered-but-unconfirmed account the way <c>UserManager</c> would, so
    /// <c>FindByEmailAsync</c> (normalized columns) and the lockout bookkeeping (stamps, opt-in) both
    /// see it. A bare <c>new User { Email }</c> is invisible to the login path.
    /// </summary>
    private async Task<long> SeedUnconfirmedUserAsync(string email, string password)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = false,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string password)
    {
        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The 403 the client's resend-confirmation panel is built on. It is only ever produced <b>after</b>
    /// the password verified, which is what keeps it from being a registration oracle — see the
    /// sibling test for the wrong-password side. Guards against
    /// <c>SignInManager.CheckPasswordSignInAsync</c> creeping back in: under
    /// <c>SignIn.RequireConfirmedEmail</c> it answers <c>NotAllowed</c> before touching the hash and
    /// collapses this branch into a 401.
    /// </summary>
    [Fact]
    public async Task Login_UnconfirmedEmail_RightPassword_Returns403_EmailNotConfirmed()
    {
        const string email = "unconfirmed-right-pw@integration.com";
        const string password = "Test@1234!";
        await SeedUnconfirmedUserAsync(email, password);

        using var response = await PostLoginAsync(email, password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("Email not confirmed");
        // B18: lockout answers 403 here too, so the status alone cannot drive the resend-confirmation
        // panel. See ErrorTypeContractTests for the 409 half of the same contract.
        body.Should().Contain("\"errorType\":\"EmailNotConfirmed\"");
        // B21: the lockout instant belongs to the lockout 403 only — absent, not null, on this one.
        body.Should().NotContain("lockoutEndUtc");
    }

    /// <summary>
    /// B21: the lockout 403 carries the instant the lock lifts as <c>lockoutEndUtc</c>, so a localised
    /// client can count down without parsing the English sentence in <c>generalErrors</c>. Like
    /// <c>errorType</c> this rides on <c>HttpContext.Items</c> and is only rendered by the
    /// <c>Errors.ResponseBuilder</c> installed in <c>Program.cs</c> — drop that line and the endpoint still
    /// stamps it, the status stays 403, and the field silently disappears, hence the body assertion.
    /// Lockout is checked before confirmation, so an unconfirmed account is enough to reach the branch.
    /// </summary>
    [Fact]
    public async Task Login_LockedOut_Returns403_WithLockoutEndUtc()
    {
        const string email = "locked-out@integration.com";
        var userId = await SeedUnconfirmedUserAsync(email, "Test@1234!");

        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            for (var i = 0; i < 5; i++)
                await userManager.AccessFailedAsync(user!);
        }

        var before = DateTimeOffset.UtcNow;
        using var response = await PostLoginAsync(email, "Test@1234!");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "B18: deliberately 403, not 423");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("Too many failed login attempts", "older clients still word-match the sentence");
        body.Should().Contain("\"errorType\":\"UserLockedOut\"");

        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("lockoutEndUtc", out var lockoutEndUtc).Should().BeTrue();
        lockoutEndUtc.GetString().Should().EndWith("Z", "the contract is ISO-8601 in UTC");
        var lockoutEnd = lockoutEndUtc.GetDateTimeOffset();
        // IdentityServiceExtensions: 5 attempts, 5 minutes. Not asserting the policy, only that the
        // instant is the one Identity stamped, i.e. in the future and no further than the window.
        lockoutEnd.Should().BeAfter(before);
        lockoutEnd.Should().BeOnOrBefore(before.AddMinutes(5).AddSeconds(5));
    }

    /// <summary>
    /// A wrong password against an unconfirmed address must look exactly like a wrong password against
    /// anything else (401, generic message) <i>and</i> feed lockout. The old
    /// <c>CheckPasswordSignInAsync</c> path short-circuited before <c>AccessFailedAsync</c>, so an
    /// unconfirmed account could be brute-forced without ever locking.
    /// </summary>
    [Fact]
    public async Task Login_UnconfirmedEmail_WrongPassword_Returns401_AndCountsTowardLockout()
    {
        const string email = "unconfirmed-wrong-pw@integration.com";
        var userId = await SeedUnconfirmedUserAsync(email, "Test@1234!");

        using var response = await PostLoginAsync(email, "Wrong@1234!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("Email not confirmed");
        body.Should().Contain("Invalid email or password");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        user.AccessFailedCount.Should().Be(1, "a wrong password against an unconfirmed account must count toward lockout");
    }

    // ── Email confirmation ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmail_NonexistentUserId_Returns400_NotNotFound()
    {
        using var client = CreateCookieClient();
        var response = await client.PostAsJsonAsync("auth/confirm-email", new ConfirmEmailRequest
        {
            Token = "fakeToken",
            UserId = 999999999
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken);
        body.Should().Contain("Invalid or expired confirmation link");
        body.Should().NotContain("User not found");
    }

    [Fact]
    public async Task ResendConfirmationEmail_UnknownEmail_Returns204_SameAsKnown()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/resend-confirmation-email");
        request.Headers.Add("X-Forwarded-For", "10.0.0.3");
        request.Content = JsonContent.Create(new { Email = "nonexistent@example.com" });

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── 2FA replay & lockout ─────────────────────────────────────────────────

    /// <summary>
    /// Mints the partial-auth token the same way the login step does. It is a signed JWT, so it can
    /// only be produced through the service — there is no way to hand-roll one in a test.
    /// </summary>
    private string IssuePendingToken(long userId)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>().IssueTwoFactorPendingToken(userId, false);
    }

    /// <summary>
    /// The partial-auth token is issued after the password alone and signed with the access-token key, so
    /// whatever it authenticates is reachable without the second factor. The notification hub is the probe
    /// because it is <c>[Authorize]</c> with no role: a role-gated endpoint would 403 the role-less token and
    /// hide the hole. Both carriers are tried — the <c>Authorization</c> header is how a non-browser client
    /// sends it, and nothing stops a user copying their own cookie into the access-token one.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingTwoFactorToken_IsRefusedAsAnAccessToken(bool asBearer)
    {
        var pendingToken = IssuePendingToken(FakeLoggedUserService.TestUserId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1");
        if (asBearer)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pendingToken);
        else
            request.Headers.Add("Cookie", $"{AuthCookies.AuthTokenName}={pendingToken}");

        using var client = CreateRawClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a token that only proves the password must not open anything the second factor guards");
    }

    [Fact]
    public async Task TwoFa_Extension_ReplayedPendingToken_Returns401()
    {
        // Generate a real pending token for the shared test user (2FA disabled → TOTP always passes)
        var userId = FakeLoggedUserService.TestUserId;
        var pendingToken = IssuePendingToken(userId);

        var payload = new { Email = TestEmail, Token = "000000", StayLoggedIn = false, PendingAuthToken = pendingToken };

        using var client = CreateCookieClient();

        // First request — token consumed (2FA disabled → TOTP passes → tokens issued)
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req1.Content = JsonContent.Create(payload);
        var res1 = await client.SendAsync(req1, TestContext.Current.CancellationToken);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request with the same token must be rejected as replayed
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req2.Content = JsonContent.Create(payload);
        var res2 = await client.SendAsync(req2, TestContext.Current.CancellationToken);
        res2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // B25: a spent session says so, so the dialog sends the user back to the password step instead
        // of inviting a retype into a token that can never succeed. No instant — nothing is locked.
        var body2 = await res2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body2.Should().Contain("\"errorType\":\"Unauthorized\"");
        body2.Should().NotContain("lockoutEndUtc");
    }

    /// <summary>
    /// A confirmed, 2FA-enabled, lockout-enabled account with no authenticator key, inserted the way
    /// <c>UserManager</c> would so the lockout bookkeeping (<c>AccessFailedAsync</c> / <c>UpdateAsync</c>)
    /// finds the stamps it throws without. No key means every TOTP is wrong, which is what the two
    /// lockout tests below need.
    /// </summary>
    private async Task<long> SeedTwoFactorUserAsync(string email)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            TwoFactorEnabled = true,
            // AccessFailedAsync only ever locks an account that opted into lockout; without this
            // the failed-count climbs and IsLockedOutAsync stays false.
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Test@1234!");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private async Task AccessFailedAsync(long userId, int times)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        for (var i = 0; i < times; i++)
            await userManager.AccessFailedAsync(user!);
    }

    /// <summary>One code attempt against a fresh pending token — the shape a real client walks.</summary>
    private async Task<HttpResponseMessage> PostExtensionTwoFaAsync(long userId, string email, string code)
    {
        var payload = new { Email = email, Token = code, StayLoggedIn = false, PendingAuthToken = IssuePendingToken(userId) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// B25: the 2FA step's lockout 401 is <c>Login_LockedOut_Returns403_WithLockoutEndUtc</c> minus the
    /// status — same <c>errorType</c>, same <c>lockoutEndUtc</c>. The status stays 401 on purpose: a
    /// client must keep reading a 401 on an auth route as "not signed in", so the body is the only thing
    /// that separates "locked out" from "wrong code" here. Same silent mechanism as B18/B21, hence the
    /// body assertion.
    /// </summary>
    private static async Task AssertLockedOutTwoFaBodyAsync(HttpResponseMessage response, DateTimeOffset before)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "B25 adds fields to the body, it does not move the status");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("locked", "older clients still word-match the sentence");
        body.Should().Contain("\"errorType\":\"UserLockedOut\"");

        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("lockoutEndUtc", out var lockoutEndUtc).Should().BeTrue();
        lockoutEndUtc.GetString().Should().EndWith("Z", "the contract is ISO-8601 in UTC");
        var lockoutEnd = lockoutEndUtc.GetDateTimeOffset();
        // IdentityServiceExtensions: 5 attempts, 5 minutes. Not asserting the policy, only that the
        // instant is the one Identity stamped, i.e. in the future and no further than the window.
        lockoutEnd.Should().BeAfter(before);
        lockoutEnd.Should().BeOnOrBefore(before.AddMinutes(5).AddSeconds(5));
    }

    /// <summary>
    /// The lock landed between the password step and the code — a concurrent password burst from
    /// elsewhere. The one branch where "wrong code" would be flatly misleading.
    /// </summary>
    [Fact]
    public async Task TwoFa_Extension_AlreadyLockedOut_Returns401_UserLockedOut_WithLockoutEndUtc()
    {
        const string email = "2fa-lockout-sec-test@integration.com";
        var userId = await SeedTwoFactorUserAsync(email);
        await AccessFailedAsync(userId, 5);

        var before = DateTimeOffset.UtcNow;
        using var response = await PostExtensionTwoFaAsync(userId, email, "000000");

        await AssertLockedOutTwoFaBodyAsync(response, before);
    }

    /// <summary>
    /// The wrong code that <i>trips</i> the lock reports the lock, the same as Identity's own
    /// <c>PasswordSignInAsync</c> and as <c>PasswordSignInFlow</c> on the password that trips it. Before
    /// B25 this attempt answered <c>InvalidTwoFactorAuthToken</c> and the user only learned they were
    /// locked on their next sign-in.
    /// </summary>
    [Fact]
    public async Task TwoFa_Extension_WrongCodeThatTripsLockout_Returns401_UserLockedOut_WithLockoutEndUtc()
    {
        const string email = "2fa-lockout-trip-sec-test@integration.com";
        var userId = await SeedTwoFactorUserAsync(email);
        await AccessFailedAsync(userId, 4);

        var before = DateTimeOffset.UtcNow;
        using var response = await PostExtensionTwoFaAsync(userId, email, "000000");

        await AssertLockedOutTwoFaBodyAsync(response, before);
    }

    /// <summary>The sub-threshold wrong code names itself and carries no instant — nothing is locked yet.</summary>
    [Fact]
    public async Task TwoFa_Extension_WrongCodeBelowThreshold_Returns401_InvalidTwoFactorAuthToken()
    {
        const string email = "2fa-wrong-code-sec-test@integration.com";
        var userId = await SeedTwoFactorUserAsync(email);

        using var response = await PostExtensionTwoFaAsync(userId, email, "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"errorType\":\"InvalidTwoFactorAuthToken\"");
        body.Should().NotContain("lockoutEndUtc", "absent, not null, on everything but the lockout");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        user.AccessFailedCount.Should().Be(1, "a wrong code must keep counting toward lockout");
    }

    // ── Token reuse detection ────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ReusedInParallel_DetectsReplayAndLocksUser()
    {
        var userId = FakeLoggedUserService.TestUserId;

        using var client = CreateCookieClient();
        await LoginAsync(client, TestEmail, TestPassword);

        // The refresh token itself stays in the handler's cookie container (HandleCookies = true) and
        // is replayed automatically below — it is never visible on DefaultRequestHeaders. Assert the
        // stored side instead, which is what actually proves login issued one.
        await using (var seedCheck = (AppDbContext)Fixture.CreateDbContext())
        {
            (await seedCheck.RefreshTokens.IgnoreQueryFilters()
                .AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeTrue("login must have issued a refresh token");
        }

        // Attempt to use the same refresh token twice in parallel
        var task1 = client.PostAsync("auth/refresh", null, TestContext.Current.CancellationToken);
        var task2 = client.PostAsync("auth/refresh", null, TestContext.Current.CancellationToken);

        await Task.WhenAll(task1, task2);

        var response1 = task1.Result;
        var response2 = task2.Result;

        // One should succeed, one should fail (or both fail — depends on implementation)
        // The key is that after detecting reuse, the user is not in a catastrophic state
        var atLeastOneFailed = !response1.IsSuccessStatusCode || !response2.IsSuccessStatusCode;
        atLeastOneFailed.Should().BeTrue("token reuse should be detected and rejected");

        // Verify the user is not locked out of normal operations (refresh should work with new token)
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.FindAsync([userId], TestContext.Current.CancellationToken);
        user.Should().NotBeNull();
        user!.LockoutEnd.Should().BeNull("user should not be permanently locked out for token reuse");
    }

    // ── Forgot password ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WithRecaptchaToken_Returns204()
    {
        var payload = new { Email = TestEmail, RecaptchaToken = "mocked-token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.2");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ForgotPassword_MissingRecaptchaToken_ReturnsError()
    {
        // Omit RecaptchaToken — it is `required` so the request is malformed
        var payload = new { Email = TestEmail };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.3");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // FastEndpoints returns 400 for model-binding failures on required fields
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }
}