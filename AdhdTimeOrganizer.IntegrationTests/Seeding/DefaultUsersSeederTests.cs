using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.@default;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.exception;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-17 — <c>DefaultUsersSeeder</c> (Core slice) plus the parts of <c>SeedUserIdProvider</c> that
/// share its env-var dependency.
/// <para>
/// <b>Deviation from review/portal/testingPrompts/DefaultUsersSeeder.md:</b> that prompt's CQ-1
/// ("failed CreateAsync doesn't return"), CQ-30 ("override path discards IdentityResult.Succeeded")
/// and CQ-31 ("HasExtensionAccess not refreshed on override") do not reproduce against the current
/// file — it already has the early <c>return;</c> after a failed <c>CreateAsync</c>, both
/// <c>UpdateAsync</c>/<c>ResetPasswordAsync</c> results are checked and logged, and
/// <c>HasExtensionAccess</c> is copied on override. It also references <c>UserRoleEnum</c>, which this
/// solution no longer has — role names live on <see cref="PortalRoleCatalog"/> now (see that class's
/// own comment on why). Below, those scenarios are written as regression guards for the fixed
/// behaviour rather than as expected-failing <c>KnownGap</c> tests.
/// </para>
/// <para>
/// Every test drives the seeder through <see cref="IAppWideDefaultSeederManager.SeedAllAsync"/> (the
/// real production entry point — <c>UserRoleSeeder</c> then <c>DefaultUsersSeeder</c>, in that
/// <c>Order</c>) rather than calling <c>DefaultUsersSeeder</c> standalone, because
/// <c>AddToRoleAsync(adminUser, "Root")</c> needs the <c>Root</c> row UserRoleSeeder creates — the
/// fixture's baseline only seeds a "User" role for the unrelated fake test user.
/// </para>
/// </summary>
[Collection("Postgres")]
public class DefaultUsersSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // Passes IdentityServiceExtensions' current policy (length 10, unique chars 4, upper/lower/digit/symbol).
    // Note the ambient ROOT_ADMIN_PASSWORD the fixture leaves set ("hbcleaning") does NOT pass this policy,
    // which is exactly the lever scenario A below uses without needing to override anything.
    private const string ValidPassword = "R00tAdm1n!Seed";
    private const string ValidPassword2 = "An0ther!Seed99";
    private const string WeakPassword = "weak";

    private IServiceScope Scope() => Fixture.UnauthenticatedFactory.Services.CreateScope();

    private static string UniqueEmail(string label) => $"{label}-{Guid.NewGuid():N}@test.com";

    // ---- A: create-path failure (CQ-1 area — now a regression guard, not a KnownGap) --------------

    [Fact]
    public async Task Seed_Create_WeakPassword_DoesNotPartiallyCreateAdmin_AndLogsFailure()
    {
        var email = UniqueEmail("weak-create");
        var (factory, logs) = NewCapturingFactory();
        await using var _ = factory;
        using var scope = factory.Services.CreateScope();

        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", WeakPassword));
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();

        await manager.SeedAllAsync();

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.AnyAsync(u => u.UserName == email)).Should().BeFalse(
            "a failed CreateAsync must not leave a partially seeded admin");
        (await db.UserRoles.AnyAsync(ur => ur.UserId == 0)).Should().BeFalse(
            "the code must not proceed to AddToRoleAsync with the unsaved user's default Id");

        logs.Messages.Should().Contain(m => m.Contains("Failed to create root admin user"));
    }

    [Fact]
    public async Task Seed_Override_WeakPassword_UpdatesFieldsButKeepsRoleAndLogsFailure()
    {
        var email = UniqueEmail("weak-override");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using (var createScope = Scope())
        {
            var manager = createScope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
            await manager.SeedAllAsync();
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var admin = await db.Users.SingleAsync(u => u.UserName == email);
        admin.Locale = AvailableLocales.En;
        admin.HasExtensionAccess = false;
        await db.SaveChangesAsync(CancellationToken);
        var adminId = admin.Id;

        var (factory, logs) = NewCapturingFactory();
        await using var _ = factory;
        using var overrideScope = factory.Services.CreateScope();
        Environment.SetEnvironmentVariable("ROOT_ADMIN_PASSWORD", WeakPassword);

        var overrideManager = overrideScope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
        await overrideManager.SeedAllAsync(overrideData: true);

        logs.Messages.Should().Contain(m => m.Contains("Failed to reset root admin password during override"));

        await using var afterDb = (AppDbContext)Fixture.CreateDbContext();
        var reloaded = await afterDb.Users.SingleAsync(u => u.Id == adminId);
        reloaded.Locale.Should().Be(AvailableLocales.Sk, "UpdateAsync ran and succeeded independently of the failed password reset");
        reloaded.HasExtensionAccess.Should().BeTrue();

        var rootRoleId = await afterDb.Roles.Where(r => r.Name == PortalRoleCatalog.Root).Select(r => r.Id).SingleAsync();
        (await afterDb.UserRoles.AnyAsync(ur => ur.UserId == adminId && ur.RoleId == rootRoleId)).Should().BeTrue(
            "a failed password reset on override must not disturb the existing role assignment");
    }

    // ---- B: idempotency — the core upsert contract -------------------------------------------------

    [Fact]
    public async Task Seed_TwiceWithoutOverride_IsIdempotent()
    {
        var email = UniqueEmail("idempotent");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using var scope = Scope();
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();

        await manager.SeedAllAsync();
        await using var db1 = (AppDbContext)Fixture.CreateDbContext();
        var adminId = await db1.Users.Where(u => u.UserName == email).Select(u => u.Id).SingleAsync();
        var defaultsCountAfterFirst = await db1.Set<TaskPriority>().CountAsync(p => p.UserId == adminId);
        defaultsCountAfterFirst.Should().BeGreaterThan(0);

        await manager.SeedAllAsync();

        await using var db2 = (AppDbContext)Fixture.CreateDbContext();
        (await db2.Users.CountAsync(u => u.UserName == email)).Should().Be(1);

        var rootRoleId = await db2.Roles.Where(r => r.Name == PortalRoleCatalog.Root).Select(r => r.Id).SingleAsync();
        (await db2.UserRoles.CountAsync(ur => ur.UserId == adminId && ur.RoleId == rootRoleId)).Should().Be(1);

        (await db2.Set<TaskPriority>().CountAsync(p => p.UserId == adminId)).Should().Be(defaultsCountAfterFirst,
            "the second run must hit the existingAdmin/no-override branch and never re-run CreateDefaultsAsync");
    }

    // ---- C: overrideData: true updates in place, and destroys nothing ------------------------------

    [Fact]
    public async Task Seed_Override_UpdatesInPlace_SameIdAndRoleSurvive_AndPasswordActuallyChanges()
    {
        var email = UniqueEmail("override-in-place");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using (var scope = Scope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
            await manager.SeedAllAsync();
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var admin = await db.Users.SingleAsync(u => u.UserName == email);
        var adminId = admin.Id;
        admin.Locale = AvailableLocales.En;
        admin.HasExtensionAccess = false;
        await db.SaveChangesAsync(CancellationToken);

        Environment.SetEnvironmentVariable("ROOT_ADMIN_PASSWORD", ValidPassword2);
        using (var scope = Scope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
            await manager.SeedAllAsync(overrideData: true);
        }

        await using var afterDb = (AppDbContext)Fixture.CreateDbContext();
        var reloaded = await afterDb.Users.SingleAsync(u => u.UserName == email);
        reloaded.Id.Should().Be(adminId, "override must update in place, never delete-and-reinsert");
        reloaded.Locale.Should().Be(AvailableLocales.Sk);
        reloaded.HasExtensionAccess.Should().BeTrue();

        var rootRoleId = await afterDb.Roles.Where(r => r.Name == PortalRoleCatalog.Root).Select(r => r.Id).SingleAsync();
        (await afterDb.UserRoles.AnyAsync(ur => ur.UserId == adminId && ur.RoleId == rootRoleId)).Should().BeTrue(
            "this is the regression CLAUDE.md's truncation warning exists for");

        using var checkScope = Scope();
        var userManager = checkScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await userManager.CheckPasswordAsync(reloaded, ValidPassword2)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(reloaded, ValidPassword)).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_Override_LeavesUnrelatedUserAndTheirRowsUntouched()
    {
        var email = UniqueEmail("override-unrelated");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using var scope = Scope();
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
        await manager.SeedAllAsync();

        long otherUserId;
        const string distinctiveText = "Definitely mine, not a default";
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var other = new User
            {
                Email = UniqueEmail("bystander"),
                EmailConfirmed = true,
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            (await userManager.CreateAsync(other, ValidPassword2)).Succeeded.Should().BeTrue();
            otherUserId = other.Id;

            db.Set<TaskPriority>().Add(new TaskPriority { UserId = otherUserId, Text = distinctiveText, Color = "#abcdef", Priority = 99 });
            await db.SaveChangesAsync(CancellationToken);
        }

        await manager.SeedAllAsync(overrideData: true);

        await using var afterDb = (AppDbContext)Fixture.CreateDbContext();
        (await afterDb.Users.AnyAsync(u => u.Id == otherUserId)).Should().BeTrue();
        var row = await afterDb.Set<TaskPriority>().SingleAsync(p => p.UserId == otherUserId && p.Priority == 99);
        row.Text.Should().Be(distinctiveText);
    }

    // ---- D: role name comes from PortalRoleCatalog (successor to CQ-29 / UserRoleEnum) -------------

    [Fact]
    public async Task Seed_CreatedAdmin_IsAssignedPortalRoleCatalogRoot()
    {
        var email = UniqueEmail("role-check");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using var scope = Scope();
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
        await manager.SeedAllAsync();

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var adminId = await db.Users.Where(u => u.UserName == email).Select(u => u.Id).SingleAsync();
        var assignedRoleNames = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == adminId
            select r.Name).ToListAsync();

        assignedRoleNames.Should().Contain(PortalRoleCatalog.Root);
    }

    // ---- E: missing configuration -------------------------------------------------------------------

    [Fact]
    public async Task Seed_MissingRootAdminPassword_OnCreatePath_LogsAndDoesNotThrow_NoPartialUser()
    {
        var email = UniqueEmail("missing-password");
        var (factory, logs) = NewCapturingFactory();
        await using var _ = factory;
        using var scope = factory.Services.CreateScope();

        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", null));
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();

        await manager.SeedAllAsync();

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.AnyAsync(u => u.UserName == email)).Should().BeFalse();
        logs.Messages.Should().Contain(m => m.Contains("Failed to seed user database"),
            "Helper.GetEnvVar throwing inside the try block must surface as a logged failure, not a silent no-op");
    }

    [Fact]
    public async Task Seed_MissingRootAdminEmail_ThrowsEnvironmentVariableMissingException()
    {
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", null), ("ROOT_ADMIN_PASSWORD", ValidPassword));
        using var scope = Scope();
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();

        // Unlike a missing password (caught, logged, returns), Helper.GetEnvVar("ROOT_ADMIN_EMAIL") runs
        // outside DefaultUsersSeeder's try/catch (it's part of the object initializer), so it propagates
        // uncaught all the way through AppWideDefaultSeederManager, which itself has no try/catch either —
        // see that class's own "exceptions propagate" doc comment. Pinning this asymmetry, not just the fact
        // that it throws.
        await Assert.ThrowsAsync<EnvironmentVariableMissingException>(() => manager.SeedAllAsync());
    }

    [Fact]
    public async Task SeedUserIdProvider_GetRootAdminUserIdAsync_ReturnsNull_WhenEmailUnset()
    {
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", null));
        using var scope = Scope();
        var provider = scope.ServiceProvider.GetRequiredService<ISeedUserProvider>();

        var result = await provider.GetRootAdminUserIdAsync(CancellationToken);

        result.Should().BeNull();
    }

    // ---- F: SEC-10 — no email in logs -----------------------------------------------------------

    [Fact]
    public async Task Seed_DuplicateEmailFailure_NeverLogsTheRawEmailAddress()
    {
        var email = UniqueEmail("duplicate");
        var (factory, logs) = NewCapturingFactory();
        await using var _ = factory;
        using var scope = factory.Services.CreateScope();

        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        // Take the username first so DefaultUsersSeeder's CreateAsync collides on it — the raw email is
        // exactly what ASP.NET Identity's default IdentityErrorDescriber embeds in DuplicateUserName/
        // DuplicateEmail, and the seeder logs the IdentityResult straight through.
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var squatter = new User { Email = email, EmailConfirmed = true, Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc };
        (await userManager.CreateAsync(squatter, ValidPassword2)).Succeeded.Should().BeTrue();

        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
        await manager.SeedAllAsync();

        logs.Messages.Should().NotBeEmpty("the collision must be logged, not silently swallowed");
        logs.Messages.Should().OnlyContain(m => !m.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    // ---- G: EmailConfirmed is deliberately true for the seeded root admin only ---------------------

    [Fact]
    public async Task Seed_CreatedAdmin_HasEmailConfirmedTrue()
    {
        var email = UniqueEmail("email-confirmed");
        using var env = new EnvVarScope(("ROOT_ADMIN_EMAIL", email), ("ROOT_ADMIN_PASSWORD", ValidPassword));

        using var scope = Scope();
        var manager = scope.ServiceProvider.GetRequiredService<IAppWideDefaultSeederManager>();
        await manager.SeedAllAsync();

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var admin = await db.Users.SingleAsync(u => u.UserName == email);
        admin.EmailConfirmed.Should().BeTrue("this is the expected shape for a system-seeded account, not a general confirmation bypass");
    }

    [Fact]
    public async Task NormallyCreatedUser_DoesNotGetEmailConfirmedTrue()
    {
        // Lightweight cross-check against the seeder's deliberate EmailConfirmed=true, at the Identity
        // level rather than through the full registration flow — see Auth/RegistrationTests.cs for that.
        using var scope = Scope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { Email = UniqueEmail("normal"), Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc };

        (await userManager.CreateAsync(user, ValidPassword)).Succeeded.Should().BeTrue();

        user.EmailConfirmed.Should().BeFalse("only DefaultUsersSeeder's root admin should bypass email confirmation");
    }

    // ---- test infrastructure ------------------------------------------------------------------------

    // SerilogConfig now passes writeToProviders: true to UseSerilog, so Serilog forwards every log
    // event to any ILoggerProvider registered in DI (previously it owned logging exclusively and
    // silently ignored one) — this is the standard Microsoft.Extensions.Logging provider seam, not a
    // DefaultUsersSeeder-specific workaround.
    private (ITestClientFactory Factory, CapturingLoggerProvider Logs) NewCapturingFactory()
    {
        var provider = new CapturingLoggerProvider();
        var factory = Fixture.CreateFactory(null, configureServices: services => services.AddSingleton<ILoggerProvider>(provider));
        return (factory, provider);
    }

    /// <summary>Captures every formatted log message across all categories, for asserting on content (SEC-10) or presence (A/E).</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                    messages.Add(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    /// <summary>
    /// Sets one or more process env vars for the scope's lifetime and restores their prior values on
    /// Dispose — required because Helper.GetEnvVar reads Environment.GetEnvironmentVariable directly,
    /// not test host configuration, so there is no per-factory config-override seam to use instead.
    /// Safe against leaking into other [Collection("Postgres")] tests because xUnit runs tests within one
    /// collection sequentially by default.
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvVarScope(params (string Name, string? Value)[] vars)
        {
            foreach (var (name, value) in vars)
            {
                _originals[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originals)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
