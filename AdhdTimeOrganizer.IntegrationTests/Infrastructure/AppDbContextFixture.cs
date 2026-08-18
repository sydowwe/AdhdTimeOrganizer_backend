using System.Security.Cryptography;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.Testing;
using Xunit;
using FrameworkLoggedUserService = Sydowwe.Framework.domain.extServiceContract.user.ILoggedUserService;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// AdhdTimeOrganizer's closed fixture over <see cref="PostgresContainerFixture{TProgram,TDbContext}"/>.
/// AppDbContext depends on this portal's own <see cref="ILoggedUserService"/> (it predates the shared
/// Framework and hasn't been migrated onto it), so <see cref="NewDbContext"/> adapts the Framework's fake
/// down to it — the adapter is only exercised for DbContexts the fixture builds directly (seeding/asserting
/// outside HTTP); requests going through the host use the real, HttpContext-backed LoggedUserService, which
/// reads the ClaimsPrincipal RoleTestAuthHandler issues.
/// </summary>
public class AppDbContextFixture : PostgresContainerFixture<Program, AppDbContext>
{
    static AppDbContextFixture()
    {
        // EcdsaKeyProvider (resolved when a request hits the real JwtBearer scheme, i.e. the
        // unauthenticated-client tests) reads this path unconditionally; the base fixture only sets the
        // env var pointing at it.
        Directory.CreateDirectory("secrets");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText("secrets/ec_private.pem", ecdsa.ExportECPrivateKeyPem());

        // AdhdTimeOrganizer-specific env vars the base fixture doesn't know about (it only sets the
        // generic auth/DB ones shared across portals).
        Environment.SetEnvironmentVariable("EXTENSION_ID", "test");
        Environment.SetEnvironmentVariable("MAIL_FROM_EMAIL", "test@test.com");
        Environment.SetEnvironmentVariable("MAIL_SMTP_PASSWORD", "test");
        Environment.SetEnvironmentVariable("MAIL_SMTP_PORT", "123");
        Environment.SetEnvironmentVariable("MAIL_SMTP_SERVER", "test");
        Environment.SetEnvironmentVariable("MAIL_SMTP_USERNAME", "test");
        Environment.SetEnvironmentVariable("OAUTH2_GOOGLE_CLIENT_ID", "test");
        Environment.SetEnvironmentVariable("OAUTH2_GOOGLE_CLIENT_SECRET", "test");
        Environment.SetEnvironmentVariable("ROOT_ADMIN_EMAIL", "root@test.com");
    }

    protected override AppDbContext NewDbContext(DbContextOptions<AppDbContext> options, FrameworkLoggedUserService user) => new(options, user, NullLogger<AppDbContext>.Instance);

    // Real password so the auth-flow tests (login/logout/refresh/change-password) can drive the actual
    // Identity + JWT pipeline through the unauthenticated factory; every other test bypasses login
    // entirely via RoleTestAuthHandler and never touches this.
    public const string TestUserPassword = "Test@1234!";

    /// <summary>
    /// Applies the three suggestion-pattern materialized views via the real
    /// <see cref="SuggestionPatternViewInstaller"/> — the same code path production uses — rather than
    /// re-reading the copied <c>.sql</c> files here, so the two can no longer drift. They are
    /// hand-written SQL rather than migration output, so <c>EnsureCreated</c> does not produce them — and
    /// <c>SuggestionPatternRefreshInterceptor</c> issues a REFRESH on every save that touches
    /// <c>PlannerTask</c>, <c>ActivityHistory</c> or <c>Calendar</c>, which fails with 42P01 when the
    /// view is missing. That made anything seeding a per-user Calendar (registration, for one)
    /// impossible to test.
    /// </summary>
    protected override Task OnSchemaCreatedAsync(AppDbContext db) =>
        SuggestionPatternViewInstaller.EnsureViewsCreatedAsync(db, NullLogger<AppDbContextFixture>.Instance);

    protected override async Task SeedFixtureAsync(AppDbContext db)
    {
        if (await db.Users.FindAsync(FakeLoggedUserService.TestUserId) is not null)
            return;

        var user = new User
        {
            Id = FakeLoggedUserService.TestUserId,
            Email = FakeLoggedUserService.TestUserEmail,
            NormalizedEmail = FakeLoggedUserService.TestUserEmail.ToUpperInvariant(),
            UserName = FakeLoggedUserService.TestUserEmail,
            NormalizedUserName = FakeLoggedUserService.TestUserEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, TestUserPassword);
        db.Users.Add(user);

        // Every endpoint without explicit roles gets Roles("User","Admin","Root") from the global
        // configurator in Program.cs, so a JWT with no role claim is authenticated but 403s on
        // everything. Tests that log in for real (AuthTestBase) therefore need the role row and the
        // assignment — the RoleTestAuthHandler bypass the other tests use fabricates roles in the
        // principal and never reaches the database.
        var userRole = new UserRole
        {
            Id = 1,
            Name = PortalRoleCatalog.User,
            NormalizedName = PortalRoleCatalog.User.ToUpperInvariant(),
            Description = "Standard user",
            IsDefault = true,
            RoleLevel = 1,
            IsAssignable = true
        };
        db.Roles.Add(userRole);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = userRole.Id });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The two outbound services every auth test would otherwise really call. Recaptcha verification fails
    /// closed (403 "Recaptcha failed." on sign-up, 400 on sign-in), and the mail sender would try a real SMTP
    /// connection, so without these swaps the whole auth suite fails on infrastructure rather than behaviour.
    /// They belong here rather than on individual clients because the flows under test span several factories.
    /// </summary>
    protected override Action<IServiceCollection> ConfigureGlobalServices => services =>
    {
        services.RemoveAll<IGoogleRecaptchaService>();
        var recaptchaMock = new Mock<IGoogleRecaptchaService>();
        recaptchaMock
            .Setup(s => s.VerifyRecaptchaAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Successful());
        services.AddSingleton(recaptchaMock.Object);

        services.RemoveAll<IUserEmailSenderService<User>>();
        var emailMock = new Mock<IUserEmailSenderService<User>>();
        emailMock.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        emailMock.Setup(s => s.SendConfirmationLinkAsync(It.IsAny<User>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        emailMock.Setup(s => s.SendEmailChangeConfirmationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        emailMock.Setup(s => s.SendPasswordResetLinkAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        emailMock.Setup(s => s.SendPasswordResetCodeAsync(It.IsAny<User>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        services.AddSingleton(emailMock.Object);
    };
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<AppDbContextFixture>;