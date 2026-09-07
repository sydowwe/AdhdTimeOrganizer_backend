using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.jobs;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-7 — the deletion behaviour of <see cref="PurgeExpiredActivityTrackingEntriesJobHandler"/>, the
/// GDPR Art. 5(1)(e) storage-limitation control for the three tracking ledgers.
/// <para>
/// <c>TrackingRouteSmokeTests.RetentionPurgeJob_IsRegisteredAndScheduled</c> already proves the job is
/// <em>registered</em> with the Scheduler after a normal boot; nothing ran it until this file. That gap is
/// the one <c>AdhdTimeOrganizer.Tracking/docs/testing.md</c> flags: the handler deletes with
/// <c>IgnoreQueryFilters()</c> on purpose, so it reaches every user's rows — which also means a predicate
/// that quietly narrowed (or widened) would delete other people's data with nothing to catch it.
/// </para>
/// <para>
/// The job is a keyed <see cref="IScheduledJobHandler"/>, not a Quartz <c>IJob</c>, so these tests boot no
/// scheduler: they build the handler the way the dispatcher would (one scope per fire, a hand-built
/// <see cref="ScheduledJobContext"/> the handler reads nothing off) and hand it an
/// <see cref="IOptions{TOptions}"/> of their own so the horizon is a test input rather than deployment
/// config. Same shape as <c>RoutineTodoListResetJobHandlerTests.RunJobAsync</c>.
/// </para>
/// <para>
/// Every read here goes through <c>IgnoreQueryFilters()</c>. <see cref="WebExtensionActivityEntry"/>
/// carries a <c>RecordDate &gt;= CurrentPartitionDate</c> filter (two years back) that would hide exactly
/// the rows these tests are asking about, and "gone from the query" is not "gone from the table" — which is
/// the distinction the whole retention control exists to make.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TrackingRetentionPurgeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- running the job -------------------------------------------------------------------------

    /// <summary>
    /// Runs the handler over a fixture-built context, with an options instance supplied by the caller.
    /// <para>
    /// The context matters. The handler takes a plain <c>DbContext</c> (the slice never sees
    /// <c>AppDbContext</c> as a type), and a fixture-built one has no application service provider, so no
    /// per-user query filter is applied to anything in it. That is what makes the cross-user assertions in
    /// this file possible to set up — and it costs nothing here, because the handler ignores filters anyway.
    /// </para>
    /// </summary>
    private async Task RunPurgeAsync(
        int retentionYears,
        bool enabled = true,
        ILogger<PurgeExpiredActivityTrackingEntriesJobHandler>? logger = null)
    {
        await using var db = CreateDbContext();
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();

        var options = Options.Create(new ActivityTrackingRetentionOptions
        {
            Enabled = enabled,
            RetentionYears = retentionYears
        });

        var handler = logger is null
            ? ActivatorUtilities.CreateInstance<PurgeExpiredActivityTrackingEntriesJobHandler>(
                scope.ServiceProvider, db, options)
            : ActivatorUtilities.CreateInstance<PurgeExpiredActivityTrackingEntriesJobHandler>(
                scope.ServiceProvider, db, options, logger);

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "tracking-retention-purge-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    // ---- 1. the age predicate --------------------------------------------------------------------

    /// <summary>
    /// The predicate itself: <c>RecordDate &lt; cutoffDate</c> — strictly older, so the boundary day
    /// survives. Four rows per ledger straddle the horizon by a day on each side, which is the only spacing
    /// that can tell a correct comparison from an off-by-one or an inverted one.
    /// <para>
    /// <c>RecordDate</c> is derived from <c>WindowStart</c> in the initializer and has a private setter, so
    /// the rows are seeded as <em>instants</em> and the dates fall out of them. Android has no
    /// <c>RecordDate</c> at all and ages out on the <c>SessionStartUtc</c> timestamp instead — a day's
    /// spacing keeps the two comparisons agreeing about which rows are old.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Purge_DeletesRowsOlderThanTheCutoff_AndKeepsTheRest()
    {
        // One year of retention: the cutoff sits a year back, and the four rows are placed around it.
        var cutoff = DateTime.UtcNow.AddYears(-1);

        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "ancient", cutoff.AddYears(-1));
            AddRow(seed, UserId, "day-before", cutoff.AddDays(-1));
            AddRow(seed, UserId, "day-after", cutoff.AddDays(1));
            AddRow(seed, UserId, "today", DateTime.UtcNow);
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1);

        var survivors = await SurvivorsAsync();
        survivors.Desktop.Should().BeEquivalentTo(new[] { "day-after", "today" });
        survivors.WebExtension.Should().BeEquivalentTo(new[] { "day-after", "today" });
        survivors.Android.Should().BeEquivalentTo(new[] { "day-after", "today" },
            "android ages out on SessionStartUtc, but on the same horizon as the other two");
    }

    // ---- 2. the cross-user sweep -----------------------------------------------------------------

    /// <summary>
    /// The <c>IgnoreQueryFilters()</c> half. This is a background sweep with no ambient caller, so it must
    /// reach every user's rows; a predicate that had picked up an ambient user (or been given a context
    /// with scoping on) would still pass every other test in this file, because they all seed as the test
    /// user.
    /// </summary>
    [Fact]
    public async Task Purge_ReachesEveryUsersRows()
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);

        await using (var seed = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            AddRow(seed, UserId, "mine-old", cutoff.AddDays(-1));
            AddRow(seed, ReminderSeedHelper.OtherUserId, "theirs-old", cutoff.AddDays(-1));
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1);

        var survivors = await SurvivorsAsync();
        survivors.Desktop.Should().BeEmpty("the sweep is deliberately cross-user");
        survivors.WebExtension.Should().BeEmpty();
        survivors.Android.Should().BeEmpty();
    }

    /// <summary>
    /// The mirror of <see cref="Purge_ReachesEveryUsersRows"/>: crossing the user boundary is not licence to
    /// delete by user. A second user's <em>recent</em> rows are as untouchable as the caller's own — the
    /// only thing that decides is age.
    /// </summary>
    [Fact]
    public async Task Purge_LeavesRecentRowsOfOtherUsersAlone()
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);

        await using (var seed = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            AddRow(seed, ReminderSeedHelper.OtherUserId, "theirs-old", cutoff.AddDays(-1));
            AddRow(seed, ReminderSeedHelper.OtherUserId, "theirs-recent", DateTime.UtcNow.AddDays(-1));
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1);

        var survivors = await SurvivorsAsync();
        survivors.Desktop.Should().BeEquivalentTo(new[] { "theirs-recent" });
        survivors.WebExtension.Should().BeEquivalentTo(new[] { "theirs-recent" });
        survivors.Android.Should().BeEquivalentTo(new[] { "theirs-recent" });
    }

    // ---- 3. the kill switch ----------------------------------------------------------------------

    /// <summary>
    /// <c>Enabled = false</c> is the audit-hold switch: the job stays scheduled and runs, and does nothing.
    /// The distinction matters operationally — a disabled purge is visible on the scheduler dashboard,
    /// whereas an unscheduled one looks the same as a lost registrar.
    /// </summary>
    [Fact]
    public async Task Purge_DoesNothingWhenDisabled()
    {
        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "ancient", DateTime.UtcNow.AddYears(-10));
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1, enabled: false);

        var survivors = await SurvivorsAsync();
        survivors.Desktop.Should().BeEquivalentTo(new[] { "ancient" }, "the kill switch gates the whole run");
        survivors.WebExtension.Should().BeEquivalentTo(new[] { "ancient" });
        survivors.Android.Should().BeEquivalentTo(new[] { "ancient" });
    }

    // ---- 4. idempotence --------------------------------------------------------------------------

    /// <summary>
    /// The job fires on a schedule, so a second run over an already-swept table is the normal case, not an
    /// edge one. It must delete nothing more and must not throw — the failure this rules out is a purge
    /// that only survives its own first run.
    /// </summary>
    [Fact]
    public async Task Purge_IsIdempotent()
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);

        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "old", cutoff.AddDays(-1));
            AddRow(seed, UserId, "recent", DateTime.UtcNow);
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1);
        var afterFirst = await SurvivorsAsync();

        await RunPurgeAsync(retentionYears: 1);
        var afterSecond = await SurvivorsAsync();

        afterFirst.Desktop.Should().BeEquivalentTo(new[] { "recent" });
        afterSecond.Desktop.Should().BeEquivalentTo(afterFirst.Desktop);
        afterSecond.WebExtension.Should().BeEquivalentTo(afterFirst.WebExtension);
        afterSecond.Android.Should().BeEquivalentTo(afterFirst.Android);
    }

    // ---- 5. across partitions --------------------------------------------------------------------

    /// <summary>
    /// <c>desktop_activity_entry</c> and <c>web_extension_activity_entry</c> are RANGE-partitioned on
    /// <c>record_date</c> (yearly partitions 2026–2030 plus a default), so a single delete has to reach rows
    /// filed under more than one partition. A predicate that swept the partition it happened to be seeded
    /// into and silently missed the next one is the failure mode partitioning introduces.
    /// <para>
    /// Two things about the setup. The horizon is pushed <em>forward</em> (a negative
    /// <c>RetentionYears</c>) rather than the rows pushed back, because the declared yearly partitions run
    /// from the current year onward — this is the only way to put old-side rows in two distinct yearly
    /// bands with a control row beyond them. And the rows are a year apart, which is what makes them
    /// different partitions rather than merely different dates.
    /// </para>
    /// <para>
    /// ⚠ The fixture builds its schema with <c>EnsureCreated</c>, which does not run the migrations SQL
    /// generator, so the tables are ordinary unpartitioned ones in the test database (the same reason
    /// <c>TrackingRouteSmokeTests</c> asserts partitioning off the model rather than off <c>pg_class</c>).
    /// This therefore pins the predicate's coverage of the date bands, not Postgres partition pruning; the
    /// partition DDL itself stays pinned by the model-level assertions there.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Purge_DeletesAcrossPartitions()
    {
        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "band-one", DateTime.UtcNow.AddYears(1));
            AddRow(seed, UserId, "band-two", DateTime.UtcNow.AddYears(2));
            AddRow(seed, UserId, "beyond-the-horizon", DateTime.UtcNow.AddYears(4));
            await seed.SaveChangesAsync(CancellationToken);
        }

        // Cutoff three years out: the first two bands are on the old side of it, the third is not.
        await RunPurgeAsync(retentionYears: -3);

        var survivors = await SurvivorsAsync();
        survivors.Desktop.Should().BeEquivalentTo(new[] { "beyond-the-horizon" },
            "both of the two yearly bands below the cutoff must go, not just the nearer one");
        survivors.WebExtension.Should().BeEquivalentTo(new[] { "beyond-the-horizon" });
    }

    // ---- 6. the third ledger ---------------------------------------------------------------------

    /// <summary>
    /// <c>android_session_data</c> — per-session app usage carrying a device id, unmistakably personal data.
    /// It was outside the purge entirely until <c>SEC-3</c> (fixed 2026-09-03), which is why it gets its own
    /// case rather than riding along on the shared assertions: it is the one ledger whose retention is not
    /// structurally guaranteed by anything else. It is not RANGE-partitioned, so it has no
    /// <c>RecordDate</c> to purge on and ages out on the <c>SessionStartUtc</c> <c>timestamptz</c> instead;
    /// and it is not covered by the two-year <c>CurrentPartitionDate</c> read floor, which applies to
    /// <c>WebExtensionActivityEntry</c> only. Drop it from the handler again and nothing else ages it out —
    /// it accumulates for the lifetime of the account.
    /// </summary>
    [Fact]
    public async Task Purge_DeletesExpiredAndroidSessionData()
    {
        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "decade-old", DateTime.UtcNow.AddYears(-10));
            AddRow(seed, UserId, "recent", DateTime.UtcNow);
            await seed.SaveChangesAsync(CancellationToken);
        }

        await RunPurgeAsync(retentionYears: 1);

        (await SurvivorsAsync()).Android.Should().BeEquivalentTo(new[] { "recent" },
            "SEC-3: the third ledger has no other retention path -- no partitioning, and none of the " +
            "two-year read floor that hides old web-extension rows");
    }

    // ---- 7. evidence that the control ran --------------------------------------------------------

    /// <summary>
    /// The one line of evidence a GDPR control leaves behind. It names all three ledgers and the configured
    /// horizon, so an operator reading the logs can tell which tables the run actually covered — the field
    /// that would have shown <c>SEC-3</c> a year earlier.
    /// </summary>
    [Fact]
    public async Task Purge_LogsTheRun()
    {
        await using (var seed = CreateDbContext())
        {
            AddRow(seed, UserId, "ancient", DateTime.UtcNow.AddYears(-10));
            await seed.SaveChangesAsync(CancellationToken);
        }

        var logger = new CapturingLogger<PurgeExpiredActivityTrackingEntriesJobHandler>();
        await RunPurgeAsync(retentionYears: 1, logger: logger);

        var message = logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Information).Which.Message;

        message.Should().Contain("desktop_activity_entry")
            .And.Contain("web_extension_activity_entry")
            .And.Contain("android_session_data",
                "the message is the only record of which ledgers a run covered");
    }

    /// <summary>
    /// <b>Pins a gap, not a rule</b> — <c>AUDIT-1</c>. The handler logs only when it deleted something, so a
    /// run with nothing to delete leaves no evidence at all that the retention control executed. That is
    /// the ordinary steady state once a deployment is caught up, which makes "did the purge run last night?"
    /// unanswerable from the logs precisely when the answer is routinely yes.
    /// <para>
    /// Intended behaviour: one unconditional Information line per run, counts included. When
    /// <c>AUDIT-1</c> is fixed, invert this assertion and drop the trait.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Status", "KnownGap")]
    public async Task Purge_LogsNothingWhenItDeletedNothing()
    {
        var logger = new CapturingLogger<PurgeExpiredActivityTrackingEntriesJobHandler>();

        await RunPurgeAsync(retentionYears: 1, logger: logger);

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Information,
            "AUDIT-1: today the run is silent when it deletes nothing -- this asserts the current " +
            "behaviour so the fix is a visible change, not a silent one");
    }

    // ---- 8. the horizon the reads assume ---------------------------------------------------------

    /// <summary>
    /// <c>MIG-6</c> — two horizons that have to agree and are declared in different places.
    /// <c>AppDbContext.CurrentPartitionDate</c> is a hard-coded <c>AddYears(-2)</c> that hides older
    /// web-extension rows from every read; the purge deletes on the configured <c>RetentionYears</c>.
    /// Configure the purge below two years and rows are deleted while still nominally readable; the
    /// reverse leaves rows retained but invisible, which reads as data loss to the user and is a retention
    /// claim the deployment cannot actually honour.
    /// <para>
    /// Asserted against the <em>bound</em> options from a real host, so this fails on a bad
    /// <c>appsettings</c> value and not just on a bad default.
    /// </para>
    /// </summary>
    [Fact]
    public void RetentionOptions_AndTheReadFilter_AgreeOnTheHorizon()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<ActivityTrackingRetentionOptions>>().Value;
        var partitionFloor = scope.ServiceProvider.GetRequiredService<AppDbContext>().CurrentPartitionDate;

        options.RetentionYears.Should().BeGreaterThanOrEqualTo(2,
            "the web-extension read filter hides anything older than two years, so a shorter retention " +
            "window deletes rows the app still considers readable");

        var cutoffDate = DateOnly.FromDateTime(options.CutoffUtc());
        (cutoffDate <= partitionFloor).Should().BeTrue(
            "the purge horizon must sit at or beyond the read floor ({0}), or the two disagree about " +
            "which rows exist -- cutoff was {1}", partitionFloor, cutoffDate);
    }

    // ---- seeding and reading ---------------------------------------------------------------------

    /// <summary>
    /// One row in each of the three ledgers, all tagged the same and all placed at the same instant, so a
    /// single expected-survivors list covers all three tables and a ledger the purge forgot stands out.
    /// <para>
    /// <c>WindowStart</c> is minute-aligned (required, and <c>RecordDate</c> derives from it) and always
    /// <c>DateTimeKind.Utc</c> — <c>SessionStartUtc</c> is a <c>timestamptz</c>, and Npgsql refuses an
    /// <c>Unspecified</c> instant there (CQ-1).
    /// </para>
    /// </summary>
    private static void AddRow(DbContext db, long userId, string tag, DateTime instant)
    {
        var windowStart = MinuteAligned(instant);

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            ProcessName = tag,
            ProductName = tag,
            WindowTitle = tag,
            ExecutablePath = $@"C:\{tag}.exe",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            Domain = tag,
            Url = null,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsFinal = true
        });

        db.Set<AndroidSessionData>().Add(new AndroidSessionData
        {
            UserId = userId,
            DeviceId = "pixel-8",
            PackageName = tag,
            AppLabel = tag,
            SessionStartUtc = windowStart,
            SessionEndUtc = windowStart.AddMinutes(1),
            DurationSeconds = 60
        });
    }

    private static DateTime MinuteAligned(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);

    /// <summary>
    /// The tags still in each table, read through <c>IgnoreQueryFilters()</c> from a fresh context. The
    /// filter lift is load-bearing on the web-extension list: its <c>RecordDate &gt;= CurrentPartitionDate</c>
    /// filter hides rows older than two years, so a filtered read would report every ancient row as
    /// "deleted" whether the purge touched it or not.
    /// </summary>
    private async Task<(List<string> Desktop, List<string> WebExtension, List<string> Android)> SurvivorsAsync()
    {
        await using var db = CreateDbContext();

        var desktop = await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Select(e => e.ProcessName).ToListAsync(CancellationToken);
        var webExtension = await db.Set<WebExtensionActivityEntry>().IgnoreQueryFilters()
            .Select(e => e.Domain).ToListAsync(CancellationToken);
        var android = await db.Set<AndroidSessionData>().IgnoreQueryFilters()
            .Select(e => e.PackageName).ToListAsync(CancellationToken);

        return (desktop, webExtension, android);
    }

    /// <summary>
    /// Captures formatted messages for the handler's own category only — passed straight into the
    /// constructor rather than registered as an <c>ILoggerProvider</c>, which would need its own host.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
