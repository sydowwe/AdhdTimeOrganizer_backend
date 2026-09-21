using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

[Collection("Postgres")]
public class AuthFunctionalTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    // ── Password change ───────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordChange_RevokesAllRefreshTokens()
    {
        const string email = "pwchange-sec-test@integration.com";
        const string password = "Test@1234!";
        const string newPassword = "NewTest@5678!";

        long userId;
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            // Normalized fields and a security stamp are not optional here: FindByEmailAsync matches on
            // NormalizedEmail, and Identity throws on a null security stamp during password change.
            var user = new User
            {
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync( TestContext.Current.CancellationToken);
            userId = user.Id;

            // Without a role the minted JWT authenticates but 403s on every endpoint, because the
            // global configurator in Program.cs puts Roles("User","Admin","Root") on anything that
            // doesn't set its own.
            var roleId = await db.Roles.Where(r => r.Name == PortalRoleCatalog.User)
                .Select(r => r.Id).FirstAsync( TestContext.Current.CancellationToken);
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = roleId });
            await db.SaveChangesAsync( TestContext.Current.CancellationToken);
        }

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, password);

        // IgnoreQueryFilters: AppDbContext scopes every IEntityWithUser to the logged-in user, and the
        // fixture's fake user is not the one this test created.
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeTrue("login must create a refresh token owned by the user who logged in");
        }

        var changePayload = new { Password = password, NewPassword = newPassword };
        (await cookieClient.PatchAsJsonAsync("user/password", changePayload, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeFalse("all refresh tokens must be revoked after password change");
        }
    }

    // ── Forced 2FA setup on first login ───────────────────────────────────────

    /// <summary>
    /// The forced-setup path: an account with <c>TwoFactorEnabled</c> but no authenticator key gets
    /// <c>RequiresTwoFactorSetup: true</c> plus the partial-auth cookie from the password step, and can
    /// provision the authenticator through <c>auth/login/2fa/setup</c> without ever holding a real
    /// session. Until <c>SetupTwoFactorForLoginEndpoint</c> existed there was no such route and the
    /// flow dead-ended here.
    /// </summary>
    [Fact]
    public async Task SetupTwoFactorForLogin_WithPendingCookie_ProvisionsAuthenticator()
    {
        const string email = "2fa-setup-func-test@integration.com";
        const string password = "Test@1234!";

        long userId;
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = new User
            {
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                EmailConfirmed = true,
                // Enabled but unprovisioned — no authenticator key yet.
                TwoFactorEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync( TestContext.Current.CancellationToken);
            userId = user.Id;
        }

        using var cookieClient = CreateCookieClient();

        var loginPayload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        loginRequest.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(loginPayload);
        var loginResponse = await cookieClient.SendAsync(loginRequest, TestContext.Current.CancellationToken);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>( TestContext.Current.CancellationToken);
        login!.RequiresTwoFactor.Should().BeTrue();
        login.RequiresTwoFactorSetup.Should().BeTrue("the account has no authenticator key yet");

        // The partial-auth cookie rides along in the client's container — the setup call carries no
        // other credential.
        var setupResponse = await cookieClient.PostAsync("auth/login/2fa/setup", null, TestContext.Current.CancellationToken);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorAuthResponse>( TestContext.Current.CancellationToken);
        setup!.TwoFactorEnabled.Should().BeTrue();
        setup.QrCode.Should().NotBeNullOrEmpty();
        setup.SharedKey.Should().NotBeNullOrEmpty("the key must be readable without decoding the QR image (B22)");
        setup.RecoveryCodes.Should().NotBeNullOrEmpty("first-time setup issues the initial recovery codes");

        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            (await userManager.GetAuthenticatorKeyAsync(user!))
                .Should().Be(setup.SharedKey, "sharedKey is the very key Identity stores, not a re-encoding of it");
        }

        // The whole point of the string: a client computes a code from it alone — no PNG decoding, no
        // server-side help — and the 2FA step accepts it. The setup call left the partial-auth cookie
        // in place, so the same client can finish the login it started.
        var totp = GenerateTotp(setup.SharedKey!, DateTimeOffset.UtcNow);
        var verifyResponse = await cookieClient.PostAsJsonAsync("auth/login/2fa",
            new { Token = totp, StayLoggedIn = false }, TestContext.Current.CancellationToken);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a TOTP generated from sharedKey must satisfy the 2FA step exactly as one from a scanned QR would");
    }

    /// <summary>
    /// RFC 6238 / 4226, six digits, 30-second step, HMAC-SHA1 — the parameters Identity's
    /// <c>AuthenticatorTokenProvider</c> verifies against, and what <c>otplib</c> defaults to on the
    /// e2e side. Written out rather than pulled in as a package because the test's claim is precisely
    /// that the bare base32 string is enough to produce a valid code.
    /// </summary>
    private static string GenerateTotp(string base32Key, DateTimeOffset at)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var key = new List<byte>();
        foreach (var c in base32Key.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            key.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }

        var step = at.ToUnixTimeSeconds() / 30;
        var counter = new byte[8];
        for (var i = 7; i >= 0; i--, step >>= 8) counter[i] = (byte)(step & 0xff);

        var hash = System.Security.Cryptography.HMACSHA1.HashData(key.ToArray(), counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken_InDb()
    {
        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, TestEmail, TestPassword);

        var userId = FakeLoggedUserService.TestUserId;
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeTrue("login must create a refresh token");
        }

        var response = await cookieClient.PostAsync("auth/logout", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeFalse("logout must revoke the refresh token in the DB");
        }
    }

    /// <summary>
    /// Regression: the portal's LogoutEndpoint used to omit <c>AllowAnonymous()</c>, so an expired access
    /// token 401'd at the auth gate and the handler never ran — the refresh token stayed live server-side
    /// and the cookies stayed in the browser, which is the opposite of what logging out is for. Logout
    /// authenticates nothing; it acts on whatever refresh token the cookie carries.
    /// </summary>
    [Fact]
    public async Task Logout_RevokesRefreshToken_WhenAccessTokenIsExpired()
    {
        var userId = FakeLoggedUserService.TestUserId;

        string refreshToken;
        string expiredAccessToken;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            refreshToken = await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .GenerateRefreshTokenAsync(userId, AuthMethodEnum.Password, false, "unknown");

            expiredAccessToken = CreateExpiredAccessToken(
                scope.ServiceProvider.GetRequiredService<IEcdsaKeyProvider>(), userId);
        }

        // Raw client: a cookie container would only ever hold the tokens the server just issued, and the
        // whole point here is a live refresh token sitting next to a dead access token.
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/logout");
        request.Headers.Add("Cookie",
            $"{AuthCookies.AuthTokenName}={expiredAccessToken}; {AuthCookies.RefreshTokenName}={refreshToken}");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "an expired access token must not stop a logout — that would strand the refresh token");

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeFalse("logout must revoke the refresh token even when the access token is expired");
        }
    }

    /// <summary>
    /// A real, correctly signed JWT for this host that is simply past its lifetime — not a malformed
    /// string, so it exercises the same JwtBearer rejection an actually-stale session hits. Expiry is
    /// well outside the handler's default five-minute clock skew.
    /// </summary>
    private static string CreateExpiredAccessToken(IEcdsaKeyProvider keyProvider, long userId)
    {
        var token = new JwtSecurityToken(
            issuer: Helper.GetEnvVar("JWT_ISSUER"),
            audience: Helper.GetEnvVar("JWT_AUDIENCE"),
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: keyProvider.GetSigningCredentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task LogoutAll_RevokesAllRefreshTokens_InDb()
    {
        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, TestEmail, TestPassword);

        var userId = FakeLoggedUserService.TestUserId;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            await svc.GenerateRefreshTokenAsync(userId, AuthMethodEnum.Password, false, "unknown");
            await svc.GenerateRefreshTokenAsync(userId, AuthMethodEnum.Password, false, "unknown");
        }

        var response = await cookieClient.PostAsync("auth/logout-all", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked, TestContext.Current.CancellationToken))
                .Should().BeFalse("logout-all must revoke every active refresh token");
        }
    }
}