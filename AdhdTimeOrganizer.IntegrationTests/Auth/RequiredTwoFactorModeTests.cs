using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

/// <summary>
/// <see cref="TwoFactorMode.Required"/>: every account must pass a second factor, whatever its own
/// <c>TwoFactorEnabled</c> flag says. The password step already branched on the mode; the two steps after
/// it did not, and both failed the account whose flag is off — the normal state of an account under this
/// mode. Setup provisioned nothing for it (so it could never sign in), and the code step accepted any code
/// for it (so the requirement could be skipped outright).
/// <para>This app runs <see cref="TwoFactorMode.Optional"/>, so each test builds its own host off the
/// fixture's with the mode overridden.</para>
/// </summary>
[Collection("Postgres")]
public class RequiredTwoFactorModeTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";
    private static readonly Uri BaseUrl = new("https://localhost/api/");

    private WebApplicationFactory<Program> RequiredModeFactory() =>
        ((WebApplicationFactory<Program>)Fixture.UnauthenticatedFactory).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<TwoFactorOptions>(o => o.Mode = TwoFactorMode.Required)));

    private static HttpClient CookieClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = BaseUrl
        });

    [Fact]
    public async Task Required_FlagOffAccount_EnrolsAtSignIn_AndSignsInWithTheNewKey()
    {
        var userId = await SeedFlagOffUserAsync("2fa-required-enrol@integration.com");
        await using var factory = RequiredModeFactory();
        using var client = CookieClient(factory);

        var login = await PostLoginAsync(client, "2fa-required-enrol@integration.com");
        login.RequiresTwoFactor.Should().BeTrue("the mode requires 2FA of every account");
        login.RequiresTwoFactorSetup.Should().BeTrue("the account has no authenticator key");

        var setupResponse = await client.PostAsync("auth/login/2fa/setup", null, TestContext.Current.CancellationToken);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorAuthResponse>(TestContext.Current.CancellationToken);
        setup!.TwoFactorEnabled.Should().BeTrue();
        setup.QrCode.Should().NotBeNullOrEmpty("before the fix a flag-off account got an empty enrolment here");
        setup.SharedKey.Should().NotBeNullOrEmpty();
        setup.RecoveryCodes.Should().NotBeNullOrEmpty();

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.Users.FindAsync([userId], TestContext.Current.CancellationToken))!.TwoFactorEnabled
                .Should().BeTrue("enrolling under Required turns the account's flag on with it");
        }

        var verify = await client.PostAsJsonAsync("auth/login/2fa",
            new { Token = AuthFunctionalTests.GenerateTotp(setup.SharedKey!, DateTimeOffset.UtcNow), StayLoggedIn = false },
            TestContext.Current.CancellationToken);
        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The skip: the code step used <c>ValidateToken</c>, which answers success for a flag-off account so the
    /// step-up dialogs can stay quiet for users without 2FA. Under Required that made any code a pass.
    /// </summary>
    [Fact]
    public async Task Required_FlagOffAccount_CannotSkipTheCodeStepWithAnyCode()
    {
        await SeedFlagOffUserAsync("2fa-required-skip@integration.com");
        await using var factory = RequiredModeFactory();
        using var client = CookieClient(factory);

        var login = await PostLoginAsync(client, "2fa-required-skip@integration.com");
        login.RequiresTwoFactor.Should().BeTrue();

        var verify = await client.PostAsJsonAsync("auth/login/2fa",
            new { Token = "000000", StayLoggedIn = false }, TestContext.Current.CancellationToken);
        verify.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an account with no authenticator has no code that can be right");
    }

    /// <summary>
    /// Outside Required, a flag-off account is never told to set up, so a setup request for one can only
    /// come from a stale token (the mode changed under it). It must not switch the account's 2FA on.
    /// </summary>
    [Fact]
    public async Task Optional_FlagOffAccount_WithSetupToken_IsRefusedAndLeftUnchanged()
    {
        var userId = await SeedFlagOffUserAsync("2fa-optional-stale@integration.com");
        string pendingToken;
        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            pendingToken = scope.ServiceProvider.GetRequiredService<IJwtService>().IssueTwoFactorPendingToken(userId, true);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/setup");
        request.Headers.Add("Cookie", $"{AuthCookies.TwoFactorTokenName}={pendingToken}");
        using var client = CreateRawClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.FindAsync([userId], TestContext.Current.CancellationToken))!.TwoFactorEnabled.Should().BeFalse();
    }

    /// <summary>A confirmed account with 2FA off and no authenticator — any self-registered user.</summary>
    private async Task<long> SeedFlagOffUserAsync(string email)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            TwoFactorEnabled = false,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private static async Task<LoginResponse> PostLoginAsync(HttpClient client, string email)
    {
        var payload = new { Email = email, Password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken))!;
    }
}
