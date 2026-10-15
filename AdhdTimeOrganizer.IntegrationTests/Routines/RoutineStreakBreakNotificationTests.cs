using AdhdTimeOrganizer.Routines.application.notification;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// P1-01 C — a broken streak's summary names the run it lost and, when a freeze can still save it, offers one.
/// <para>
/// Real period, real reset job, real notification pipeline, and the assertions are on the persisted
/// notification row and on the period row after the freeze — the payload's <c>Streak</c> is already 0 after a
/// break, so the extra fields are the only place the lost run survives, and an offer the endpoint would refuse
/// is worse than none.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineStreakBreakNotificationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const int RunBeforeTheMiss = 3;

    /// <summary>
    /// A weekly period whose reset is due, preceded by <see cref="RunBeforeTheMiss"/> met cycles in its history,
    /// with one undone item — so the reset scores a miss, and with no grace days that miss breaks the run.
    /// </summary>
    private async Task<long> SeedBreakingPeriodAsync(int freezeBudget)
    {
        await using var db = CreateDbContext();
        var lastResetAt = DateTime.UtcNow.Date.AddDays(-8);

        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = $"Breaking period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            Streak = RunBeforeTheMiss,
            BestStreak = RunBeforeTheMiss,
            FreezeBudget = freezeBudget,
            FreezesRemaining = freezeBudget,
            LastResetAt = lastResetAt
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        var lastStart = DateOnly.FromDateTime(lastResetAt);
        for (var cycle = RunBeforeTheMiss; cycle >= 1; cycle--)
        {
            var start = lastStart.AddDays(-7 * cycle);
            db.Set<RoutinePeriodCompletion>().Add(new RoutinePeriodCompletion
            {
                TimePeriodId = period.Id,
                PeriodStart = start,
                PeriodEnd = start.AddDays(7),
                CompletedCount = 2,
                TotalCount = 2
            });
        }

        var role = new ActivityRole { UserId = UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        var activity = new Activity { UserId = UserId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        db.Set<RoutineTodoList>().Add(new RoutineTodoList
        {
            UserId = UserId, ActivityId = activity.Id, TimePeriodId = period.Id, IsDone = false
        });
        await db.SaveChangesAsync(CancellationToken);

        return period.Id;
    }

    private async Task RunResetJobAsync()
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(
            scope.ServiceProvider, typeof(RoutineTodoListResetJobHandler));

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-streak-break-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private async Task<Notification> PeriodEndedRowAsync(long periodId)
    {
        await using var db = CreateDbContext();
        var rows = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == UserId && n.Type == RoutinesNotificationTypes.RoutinePeriodEnded)
            .ToListAsync(CancellationToken);
        return rows.Should().ContainSingle(n =>
            JsonHelper.Deserialize<RoutinePeriodEndedPayload>(n.PayloadJson)!.PeriodId == periodId).Subject;
    }

    private (string En, string Sk) Bodies(Notification row)
    {
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        return (renderer.Render(row.Type, row.PayloadJson, AvailableLocales.En).Body,
            renderer.Render(row.Type, row.PayloadJson, AvailableLocales.Sk).Body);
    }

    [Fact(DisplayName = "P1-01 C: a broken streak with budget left carries the lost run and offers the freeze, which restores it")]
    public async Task BrokenStreak_WithBudget_OffersTheFreeze_AndTheFreezeRestoresTheRun()
    {
        var periodId = await SeedBreakingPeriodAsync(freezeBudget: 2);

        await RunResetJobAsync();

        var row = await PeriodEndedRowAsync(periodId);
        var payload = JsonHelper.Deserialize<RoutinePeriodEndedPayload>(row.PayloadJson)!;
        payload.Outcome.Should().Be(RoutineStreakOutcome.Broken);
        payload.Streak.Should().Be(0, "after a break the streak itself says nothing");
        payload.LostStreak.Should().Be(RunBeforeTheMiss);
        payload.FreezesRemaining.Should().Be(2);
        payload.CanFreeze.Should().BeTrue();

        var (en, sk) = Bodies(row);
        en.Should().EndWith(" Your streak of 3 broke — you have 2 skips left to save it.");
        sk.Should().EndWith(" Séria 3 sa prerušila — na jej záchranu vám zostávajú 2 vynechania.");

        // The offer is only worth making if taking it works: spend it through the endpoint on the miss the reset
        // just wrote, and read the period row back.
        DateOnly newestStart;
        await using (var db = CreateDbContext())
        {
            newestStart = await db.Set<RoutinePeriodCompletion>().AsNoTracking()
                .Where(c => c.TimePeriodId == periodId)
                .MaxAsync(c => c.PeriodStart, CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            $"/api/routine-todo-list/time-period/{periodId}/streak-freeze",
            new { periodStart = newestStart.ToString("yyyy-MM-dd") }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = CreateDbContext();
        var period = await verify.Set<RoutineTimePeriod>().AsNoTracking().SingleAsync(p => p.Id == periodId, CancellationToken);
        period.Streak.Should().Be(RunBeforeTheMiss, "the freeze the notification offered brings back exactly the run it named");
        period.FreezesRemaining.Should().Be(1);
    }

    [Fact(DisplayName = "P1-01 C: with no budget the summary keeps the plain broken sentence")]
    public async Task BrokenStreak_WithNoBudget_KeepsThePlainText()
    {
        var periodId = await SeedBreakingPeriodAsync(freezeBudget: 0);

        await RunResetJobAsync();

        var row = await PeriodEndedRowAsync(periodId);
        var payload = JsonHelper.Deserialize<RoutinePeriodEndedPayload>(row.PayloadJson)!;
        payload.LostStreak.Should().Be(RunBeforeTheMiss, "the lost run is named either way");
        payload.CanFreeze.Should().BeFalse("a freeze the endpoint would refuse must not be offered");

        var (en, sk) = Bodies(row);
        en.Should().EndWith(" The streak was broken.");
        sk.Should().EndWith(" Séria bola prerušená.");
    }

    /// <summary>
    /// A row persisted before the three fields existed deserializes to their defaults and renders today's text —
    /// the payload is a persisted contract, so the change has to be additive.
    /// </summary>
    [Fact(DisplayName = "P1-01 C: an older broken-streak row without the new fields renders the plain sentence")]
    public void OlderRow_WithoutTheNewFields_RendersThePlainSentence()
    {
        const string legacy = """{"periodId":1,"periodName":"Weekly","completedCount":1,"totalCount":3,"streak":0,"outcome":3}""";

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.Render(RoutinesNotificationTypes.RoutinePeriodEnded, legacy, AvailableLocales.En).Body
            .Should().EndWith(" The streak was broken.");
    }
}
