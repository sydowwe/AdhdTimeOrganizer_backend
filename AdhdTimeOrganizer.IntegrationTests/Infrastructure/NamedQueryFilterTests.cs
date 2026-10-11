using AdhdTimeOrganizer.Core.infrastructure.persistence;
using System.Security.Claims;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
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
    /// The three profile entities carrying the mirrored user-scope concern. None of them is
    /// IEntityWithUser, so <see cref="EveryUserOwnedEntity_CarriesTheUserScopeFilterName"/> does not
    /// cover them; this is their equivalent.
    /// </summary>
    private static readonly Type[] ProfileUserScopedTypes =
    [
        typeof(ActivityBacklogProfile),
        typeof(ActivityBucketListProfile),
        typeof(ActivityProjectProfile)
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
    /// The converse of <see cref="ArchivalFilter_UsesTheSameNameOnThePrincipalAndEveryDependent"/>:
    /// <b>every</b> entity holding a foreign key into <see cref="Activity"/> is explicitly classified as
    /// either hiding with an archived activity or surviving it, and nothing else in the model carries the
    /// archival name at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this closes.</b> Which dependents mirror <c>ActivityArchived</c> is a cross-slice policy
    /// decided entirely in <c>AppDbContext.OnModelCreating</c> — a host file the author of a new slice
    /// entity is not editing, and cannot see from the slice. The entity itself carries no signal either
    /// way, so a new table hanging off <c>Activity</c> silently defaults to <em>surviving</em>, which is
    /// only sometimes right. The test above pinned that the six known dependents carry the filter; it
    /// could not notice a seventh dependent that should have.
    /// </para>
    /// <para>
    /// <b>Why it matters beyond tidiness.</b> <c>IActivityReferenceSource</c> implementations must lift
    /// this filter in <c>ReferencingActivityIds</c> if their table carries it, or an archived activity
    /// under-reports <c>usageCount</c> — and <c>canDelete</c> is <c>usageCount == 0</c> over a family of
    /// FKs that cascade. So "does my table mirror archival?" is a question a slice author has to answer
    /// correctly while looking at a file that does not tell them. This turns it into a failing test.
    /// </para>
    /// <para>
    /// <b>The rule behind the split.</b> Archival hides an activity from every <em>picker</em> while
    /// keeping the rows that reference it rendering its name. So a dependent mirrors the filter only when
    /// it is itself picker-like or descriptive of the activity — the three profiles, and the two
    /// suggestion read-models that feed the planner's pickers. Everything recording that something
    /// <em>happened</em> must survive: that is the whole reason archiving exists rather than deleting.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryActivityDependent_IsClassifiedAgainstTheArchivalFilter()
    {
        var model = HostModel();

        var dependents = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Activity))
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .ToList();

        dependents.Should().NotBeEmpty("a model with no activity dependents means this guard is vacuous");

        var unclassified = dependents
            .Where(type => !ArchivalFilteredTypes.Contains(type) && !SurvivesArchivingTypes.Contains(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unclassified.Should().BeEmpty(
            "a new table hanging off Activity has to declare which side of archiving it is on, and the " +
            "entity carries no signal either way -- the decision lives in AppDbContext.OnModelCreating. " +
            "Add it to ArchivalFilteredTypes (and mirror the filter in AppDbContext) if it must vanish " +
            "with an archived activity, or to SurvivesArchivingTypes if it records that something " +
            "happened. Read the rule in this test's remarks first, and check whether its " +
            "IActivityReferenceSource has to lift the filter");

        var unexpected = model.GetEntityTypes()
            .Where(entityType => entityType.FindDeclaredQueryFilter(AppQueryFilters.ActivityArchived) is not null)
            .Select(entityType => entityType.ClrType)
            .Except(ArchivalFilteredTypes)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unexpected.Should().BeEmpty(
            "ArchivalFilteredTypes is the inventory of the archival concern, so an entity carrying the " +
            "filter without appearing in it means the two have drifted -- and every reader of that table " +
            "is now silently missing an archived activity's rows");
    }

    /// <summary>
    /// The other side of the classification: activity dependents that deliberately stay visible when
    /// their activity is archived, because each records that something <b>happened</b>. Hiding any of
    /// them would make archiving destructive in effect while claiming not to be — an archived activity's
    /// history would vanish from the dashboards it is the whole point of keeping.
    /// </summary>
    /// <remarks>
    /// The timer presets are the least obvious three. A preset naming an archived activity still renders
    /// that activity's name in the timer list, which is the intended behaviour of archiving; it is the
    /// activity <em>pickers</em> that exclude it, per-endpoint, not the rows pointing at it.
    /// </remarks>
    private static readonly Type[] SurvivesArchivingTypes =
    [
        // Core — the presets keep naming the activity; the exclusion lives in the four pickers.
        typeof(TimerPreset),
        typeof(PomodoroTimerPreset),

        // History — the rows archiving exists to preserve.
        typeof(ActivityHistory),

        // Planning — the three concrete task shapes. A planned or completed task is a record of intent.
        typeof(PlannerTask),
        typeof(RepeatingPlannerTask),
        typeof(TemplatePlannerTask),

        // FO11. The user's own "not any more" — it must still hold if the activity is archived and brought back.
        typeof(AdhdTimeOrganizer.Planning.domain.model.entity.suggestion.PlannerSuggestionDismissal),

        // Routines / TodoLists.
        typeof(RoutineTodoList),
        typeof(TodoListItem),

        // Tracking — raw ingest mappings; a mapping that stopped resolving would re-attribute new entries.
        typeof(TrackerAndroidMappingByPattern),
        typeof(TrackerDesktopMappingByPattern),

        // ActivityProfiles — these two are records, not profiles: a drawn suggestion and a memory.
        typeof(LeisureSuggestionRecord),
        typeof(MemoryAnchor)
    ];

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
    /// The three Activity*Profile entities carry two concerns each — archival and the mirrored user
    /// scope — and both must be declared under their own name. Unnamed, the second call would replace
    /// the first: the exact regression <see cref="Activity_CarriesBothTheUserScopeAndArchivedFilters"/>
    /// pins on the principal, one join away.
    /// </summary>
    [Fact]
    public void ActivityProfiles_CarryBothTheMirroredUserScopeAndArchivedFilters()
    {
        var model = HostModel();

        var missing = ProfileUserScopedTypes
            .Where(type => model.FindEntityType(type)!
                .FindDeclaredQueryFilter(AppQueryFilters.ActivityProfileUserScope) is null)
            .Select(type => type.Name)
            .ToList();

        missing.Should().BeEmpty(
            "a profile row carries no UserId of its own, so ApplyUserQueryFilters never sees it -- " +
            "without this mirrored filter the only thing scoping these tables is whatever each endpoint " +
            "remembers to write by hand, which is how GET /api/activity-backlog-profile came to return " +
            "every user's rows");

        var stillArchivalFiltered = ProfileUserScopedTypes
            .Where(type => model.FindEntityType(type)!
                .FindDeclaredQueryFilter(AppQueryFilters.ActivityArchived) is null)
            .Select(type => type.Name)
            .ToList();

        stillArchivalFiltered.Should().BeEmpty(
            "the user-scope filter must COEXIST with the archival one -- applied unnamed it would have " +
            "replaced it, un-hiding archived profiles with no error");
    }

    /// <summary>
    /// The behavioural half, asserted on rows: a signed-in caller sees only their own profiles even on
    /// a query that names no ownership predicate at all, and the seeder/merge/export paths — which run
    /// with no ambient user — still see everything.
    /// </summary>
    [Fact]
    public async Task ProfileUserScopeFilter_HidesForeignProfiles_AndKeepsTheUnscopedEscape()
    {
        var userIdA = await CreateUserAsync("profile-filter-a@test.com");
        var userIdB = await CreateUserAsync("profile-filter-b@test.com");

        long profileIdA;
        long profileIdB;
        await using (var seed = CreateDbContext())
        {
            profileIdA = await SeedBacklogProfileAsync(seed, userIdA, $"profile-of-a-{Guid.NewGuid():N}");
            profileIdB = await SeedBacklogProfileAsync(seed, userIdB, $"profile-of-b-{Guid.NewGuid():N}");
        }

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userIdA.ToString())], "Test"))
            };

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var visible = await db.Set<ActivityBacklogProfile>()
                .Select(p => p.Id)
                .ToListAsync(CancellationToken);

            visible.Should().Contain(profileIdA);
            visible.Should().NotContain(profileIdB,
                "the query names no ownership predicate -- the global filter is the only thing between " +
                "this read and every user's profiles");
        }
        finally
        {
            accessor.HttpContext = null;
        }

        // The other half -- that a context with no ambient user still sees every row -- cannot be
        // asserted through this host: FakeLoggedUserService.IsAuthenticated is hard-coded true and its
        // GetUserId falls back to TestUserId, so ScopeUserId is never null here. Assert the escape on the
        // predicate instead. Dropping it is what would silently empty the dev seeders, the merge/repoint
        // seam and the export/erasure paths, none of which run inside a request.
        var predicate = HostModel().FindEntityType(typeof(ActivityBacklogProfile))!
            .FindDeclaredQueryFilter(AppQueryFilters.ActivityProfileUserScope)!
            .Expression.ToString();

        predicate.Should().Contain("null",
            "without the ScopeUserId == null escape every read that runs outside a request returns " +
            "nothing -- no error, no log line, just an empty result where the seeders expect rows");
        predicate.Should().Contain(nameof(AppDbContext.ScopeUserId),
            "the id must be read off the executing DbContext, not a captured service -- EF folds a " +
            "member read off a constant into the cached SQL, so the first caller's id would scope " +
            "every later execution of that query shape, for every user");
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

    /// <summary>
    /// Seeded through a fixture context, which carries no user filter at all — that is what makes
    /// writing one profile per user in a single pass possible.
    /// </summary>
    private async Task<long> SeedBacklogProfileAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);

        var locationType = new ActivityLocationType { UserId = userId, Text = $"{name} Home" };
        var weatherDependency = new ActivityWeatherDependency { UserId = userId, Text = $"{name} Indoor" };
        var costTier = new ActivityExpectedCostTier { UserId = userId, Text = $"{name} Free" };
        db.Set<ActivityLocationType>().Add(locationType);
        db.Set<ActivityWeatherDependency>().Add(weatherDependency);
        db.Set<ActivityExpectedCostTier>().Add(costTier);
        await db.SaveChangesAsync(CancellationToken);

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activity.Id,
            LocationTypeId = locationType.Id,
            WeatherDependencyId = weatherDependency.Id,
            ExpectedCostTierId = costTier.Id,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return profile.Id;
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
