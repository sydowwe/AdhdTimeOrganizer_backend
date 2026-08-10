using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.response.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.security;
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
            await db.SaveChangesAsync();
            userId = user.Id;

            // Without a role the minted JWT authenticates but 403s on every endpoint, because the
            // global configurator in Program.cs puts Roles("User","Admin","Root") on anything that
            // doesn't set its own.
            var roleId = await db.Roles.Where(r => r.Name == PortalRoleCatalog.User)
                .Select(r => r.Id).FirstAsync();
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = roleId });
            await db.SaveChangesAsync();
        }

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, password);

        // IgnoreQueryFilters: AppDbContext scopes every IEntityWithUser to the logged-in user, and the
        // fixture's fake user is not the one this test created.
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must create a refresh token owned by the user who logged in");
        }

        var changePayload = new { Password = password, NewPassword = newPassword };
        (await cookieClient.PatchAsJsonAsync("user/password", changePayload))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked))
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
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using var cookieClient = CreateCookieClient();

        var loginPayload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        loginRequest.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(loginPayload);
        var loginResponse = await cookieClient.SendAsync(loginRequest);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        login!.RequiresTwoFactor.Should().BeTrue();
        login.RequiresTwoFactorSetup.Should().BeTrue("the account has no authenticator key yet");

        // The partial-auth cookie rides along in the client's container — the setup call carries no
        // other credential.
        var setupResponse = await cookieClient.PostAsync("auth/login/2fa/setup", null);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorAuthResponse>();
        setup!.TwoFactorEnabled.Should().BeTrue();
        setup.QrCode.Should().NotBeNullOrEmpty();
        setup.RecoveryCodes.Should().NotBeNullOrEmpty("first-time setup issues the initial recovery codes");

        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            (await userManager.GetAuthenticatorKeyAsync(user!))
                .Should().NotBeNullOrEmpty("setup must persist the authenticator key");
        }
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
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must create a refresh token");
        }

        var response = await cookieClient.PostAsync("auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
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

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "an expired access token must not stop a logout — that would strand the refresh token");

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
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

        var response = await cookieClient.PostAsync("auth/logout-all", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeFalse("logout-all must revoke every active refresh token");
        }
    }
}