using System.Security.Claims;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins that every global query filter in the model is <b>named</b> (EF 10) and named <b>per concern</b>
/// rather than per entity, which is what makes <c>IgnoreQueryFilters([name])</c> mean anything.
///
/// <para><b>Why this needs a test.</b> Every failure mode here is silent. An unnamed filter is not an
/// error — it just cannot be lifted selectively, so the only way past it stays the argument-less
/// <c>IgnoreQueryFilters()</c>, which drops user scoping along with whatever the caller actually
/// wanted. A misspelled name is not an error either: EF does not check that a key handed to
/// <c>IgnoreQueryFilters</c> matches a filter that exists, so the call silently ignores nothing. And
/// naming the same concern differently on a principal and its dependent leaves the other end of a join
/// still filtered, which surfaces as a row that loads with an empty include.</para>
///
/// <para><b>The regression the names fixed.</b> Unnamed, a second <c>HasQueryFilter</c> on an entity
/// <i>replaces</i> the first. <c>AppDbContext.OnModelCreating</c> runs after <c>base.OnModelCreating</c>,
/// so its archival filter on <c>Activity</c> silently overwrote the per-user filter
/// <c>ApplyUserQueryFilters</c> had just applied — leaving the table readable across users, with no
/// exception and no log line. <see cref="Activity_CarriesBothTheUserScopeAndArchivedFilters"/> is what
/// notices if that comes back.</para>
/// </summary>
[Collection("Postgres")]
public class NamedQueryFilterTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// The six entities carrying the archival concern — the principal plus every dependent whose rows
    /// must disappear with it. All six must answer to one name.
    /// </summary>
    private static readonly Type[] ArchivalFilteredTypes =
    [
        typeof(Activity),
        typeof(ActivityBacklogProfile),
        typeof(ActivityBucketListProfile),
        typeof(ActivityProjectProfile),
        typeof(PlannerSuggestionFromActivityHistory),
        typeof(PlannerSuggestionFromPlannerTask)
    ];

    /// <summary>
    /// A host-built model, not a fixture-built one: fixture contexts are constructed straight off a
    /// <c>DbContextOptionsBuilder</c> and have no application service provider, so
    /// <c>UserScopingOptions</c> resolves to null and <b>no</b> per-user filter is applied to them at
    /// all (see <c>BaseDbContext.ApplyUserScopingIfEnabled</c>). Only the host model has both concerns.
    /// </summary>
    private IModel HostModel()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
    }

    [Fact]
    public void EveryQueryFilterInTheModel_IsNamed()
    {
        var anonymous = HostModel().GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredQueryFilters()
                .Where(filter => filter.IsAnonymous)
                .Select(filter => $"{entityType.ClrType.Name}: {filter.Expression}"))
            .ToList();

        anonymous.Should().BeEmpty(
            "an unnamed filter cannot be lifted selectively, so the only way past it is the " +
            "argument-less IgnoreQueryFilters(), which also drops user scoping. Name it with a " +
            "QueryFilters/AppQueryFilters constant for its concern");
    }

    [Fact]
    public void EveryUserOwnedEntity_CarriesTheUserScopeFilterName()
    {
        var unscoped = HostModel().GetEntityTypes()
            .Where(entityType => typeof(IEntityWithUser).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => entityType.FindDeclaredQueryFilter(QueryFilters.UserScope) is null)
            .Select(entityType => entityType.ClrType.Name)
            .ToList();

        unscoped.Should().BeEmpty(
            "every IEntityWithUser is scoped by ApplyUserQueryFilters -- and WebExtensionActivityEntry, " +
            "which is excluded from it and scoped by hand in AppDbContext, must reuse the same name or " +
            "an IgnoreQueryFilters([QueryFilters.UserScope]) would leave exactly that one table scoped");
    }

    /// <summary>
    /// The two concerns on <c>Activity</c> are independent and must both be declared: unnamed, the
    /// second call replaced the first and the table lost user scoping outright.
    /// </summary>
    [Fact]
    public void Activity_CarriesBothTheUserScopeAndArchivedFilters()
    {
        var activity = HostModel().FindEntityType(typeof(Activity))!;

        activity.FindDeclaredQueryFilter(QueryFilters.UserScope).Should().NotBeNull(
            "the archival filter is applied after base.OnModelCreating -- unnamed, it overwrites the " +
            "per-user filter and every user's activities become readable by any signed-in caller");
        activity.FindDeclaredQueryFilter(AppQueryFilters.ActivityArchived).Should().NotBeNull();
    }

    [Fact]
    public void ArchivalFilter_UsesTheSameNameOnThePrincipalAndEveryDependent()
    {
        var model = HostModel();

        var missing = ArchivalFilteredTypes
            .Where(type => model.FindEntityType(type)!
                .FindDeclaredQueryFilter(AppQueryFilters.ActivityArchived) is null)
            .Select(type => type.Name)
            .ToList();

        missing.Should().BeEmpty(
            "one name per concern is what lets a single IgnoreQueryFilters call reveal an archived " +
            "activity together with its profiles -- named per entity, the principal reappears and the " +
            "dependent stays filtered, which reads as a row whose include is empty");
    }

    /// <summary>
    /// The behavioural half, asserted on rows: lifting the archival concern by name must reveal
    /// archived rows and must <b>not</b> lift user scoping with it. That separation is the entire point
    /// of splitting combined predicates into one named filter per concern.
    /// </summary>
    [Fact]
    public async Task IgnoringTheArchivalFilter_RevealsArchivedRows_ButStillOnlyTheCallersOwn()
    {
        var userIdA = await CreateUserAsync("named-filter-a@test.com");
        var userIdB = await CreateUserAsync("named-filter-b@test.com");

        var mine = $"archived-of-a-{Guid.NewGuid():N}";
        var theirs = $"archived-of-b-{Guid.NewGuid():N}";

        // Seeded through a fixture context, which has no per-user filter -- that is what makes writing
        // rows for two different users in one go possible.
        await using (var seed = CreateDbContext())
        {
            await SeedArchivedActivityAsync(seed, userIdA, mine);
            await SeedArchivedActivityAsync(seed, userIdB, theirs);
        }

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        try
        {
            // HttpContextAccessor's backing store is a static AsyncLocal, so this is the ambient
            // principal for the ILoggedUserService the model reads, exactly as it would be in a request.
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userIdA.ToString())], "Test"))
            };

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var withBothFilters = await db.Set<Activity>()
                .Select(a => a.Name)
                .ToListAsync(CancellationToken);

            var archivalLifted = await db.Set<Activity>()
                .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
                .Select(a => a.Name)
                .ToListAsync(CancellationToken);

            withBothFilters.Should().NotContain(mine, "the archival filter hides archived rows by default");

            archivalLifted.Should().Contain(mine,
                "naming the concern is what lets a caller lift exactly it -- if this is empty the name " +
                "handed to IgnoreQueryFilters does not match the one on the filter, which EF does not " +
                "report as an error");
            archivalLifted.Should().NotContain(theirs,
                "lifting the archival concern must NOT lift user scoping -- the two were split into " +
                "separate named filters precisely so one can be dropped without the other");
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    /// <summary>
    /// The entity that carries a hand-written user filter alongside a second concern. Both must be
    /// declared separately; combined into one predicate, dropping the retention bound for a historical
    /// export would drop user scoping with it.
    /// </summary>
    [Fact]
    public void WebExtensionActivityEntry_DeclaresItsTwoConcernsSeparately()
    {
        var entry = HostModel().FindEntityType(typeof(WebExtensionActivityEntry))!;

        var partitionFilter = entry.FindDeclaredQueryFilter(AppQueryFilters.PartitionDate);
        var userFilter = entry.FindDeclaredQueryFilter(QueryFilters.UserScope);

        partitionFilter.Should().NotBeNull();
        userFilter.Should().NotBeNull(
            "this entity is excluded from ApplyUserQueryFilters, so the hand-written half is the only " +
            "thing scoping it -- without it every user's browsing history is readable by any caller");

        partitionFilter!.Expression.ToString().Should().Contain(nameof(WebExtensionActivityEntry.RecordDate));
        userFilter!.Expression.ToString().Should().Contain(nameof(WebExtensionActivityEntry.UserId));
        partitionFilter.Expression.ToString().Should().NotContain(nameof(WebExtensionActivityEntry.UserId),
            "the two concerns must be separate filters, not one &&-ed predicate under a single name");
    }

    private async Task SeedArchivedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<Activity>().Add(new Activity { UserId = userId, Name = name, RoleId = role.Id, IsArchived = true });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<long> CreateUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return existing.Id;

        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }
}
