using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.application.notification;
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
/// P3-02: a met period's summary says when the run is a new best or lands on a milestone, once, in the run's
/// own unit.
/// <para>
/// This uses a real period, the real reset job and the real notification pipeline, and asserts on the persisted
/// bell row. Whether a cycle is a new best is decided inside the reset and survives only in the payload, so a
/// test that stops at the notifier call would miss the decision getting lost on the way to the row.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineStreakMilestoneNotificationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// A weekly period whose reset is due, with its one item done, so the reset extends the streak by one.
    /// <paramref name="record"/> is the best a previous run set, which this run is chasing.
    /// </summary>
    private async Task<long> SeedMetWeeklyPeriodAsync(int streak, int record)
    {
        await using var db = CreateDbContext();

        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = $"Milestone period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            Streak = streak,
            BestStreak = Math.Max(streak, record),
            BestStreakBeforeRun = record,
            LastResetAt = DateTime.UtcNow.Date.AddDays(-8)
        };
        db.Set<RoutineTimePeriod>().Add(period);

        var role = new ActivityRole { UserId = UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        var activity = new Activity { UserId = UserId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        db.Set<RoutineTodoList>().Add(new RoutineTodoList
        {
            UserId = UserId, ActivityId = activity.Id, TimePeriodId = period.Id, IsDone = true
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
            CorrelationId = "routine-streak-milestone-test",
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

    [Theory(DisplayName = "P3-02: the nightly reset's bell row carries the new-best and milestone flags, and the body says it once")]
    // 5 → 6 passes a previous record of 5. Six weeks isn't a milestone.
    [InlineData(5, 5, true, false,
        " New personal best: 6 weeks in a row (previous best: 5 weeks).",
        " Nový osobný rekord: 6 týždňov po sebe (doterajší rekord: 5 týždňov).")]
    // 3 → 4 passes a record of 3 and lands on the first weekly milestone: one sentence, with the best folded in.
    [InlineData(3, 3, true, true,
        " Milestone: 4 weeks in a row — your longest yet!",
        " Míľnik: 4 týždne po sebe — vaša doteraz najdlhšia séria!")]
    // A milestone well short of the record.
    [InlineData(3, 10, false, true,
        " Milestone: 4 weeks in a row!",
        " Míľnik: 4 týždne po sebe!")]
    // The second cycle of a record run: already announced, so just the plain sentence.
    [InlineData(6, 5, false, false,
        " Streak continues: 7.",
        " Séria pokračuje: 7.")]
    public async Task ResetJob_ExtendedStreak_PersistsTheFlags_AndRendersOneSentence(
        int streak, int record, bool isNewBest, bool isMilestone, string enSuffix, string skSuffix)
    {
        var periodId = await SeedMetWeeklyPeriodAsync(streak, record);

        await RunResetJobAsync();

        var row = await PeriodEndedRowAsync(periodId);
        var payload = JsonHelper.Deserialize<RoutinePeriodEndedPayload>(row.PayloadJson)!;
        payload.Outcome.Should().Be(RoutineStreakOutcome.Extended);
        payload.Streak.Should().Be(streak + 1);
        payload.IsNewBest.Should().Be(isNewBest);
        payload.PreviousBest.Should().Be(isNewBest ? record : 0);
        payload.IsMilestone.Should().Be(isMilestone);
        payload.PeriodLengthInDays.Should().Be(7);

        var (en, sk) = Bodies(row);
        en.Should().EndWith(enSuffix);
        sk.Should().EndWith(skSuffix);

        await using var verify = CreateDbContext();
        var period = await verify.Set<RoutineTimePeriod>().AsNoTracking().SingleAsync(p => p.Id == periodId, CancellationToken);
        period.BestStreakBeforeRun.Should().Be(record, "only a run's first cycle moves its record to beat");
    }

    /// <summary>
    /// A row persisted before the four fields existed deserializes to their defaults and renders today's
    /// sentence. The payload is a persisted contract, so the change has to be additive.
    /// </summary>
    [Fact(DisplayName = "P3-02: an older extended row without the new fields renders the plain sentence")]
    public void OlderRow_WithoutTheNewFields_RendersThePlainSentence()
    {
        const string legacy = """{"periodId":1,"periodName":"Weekly","completedCount":3,"totalCount":3,"streak":30,"outcome":1}""";

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.Render(RoutinesNotificationTypes.RoutinePeriodEnded, legacy, AvailableLocales.En).Body
            .Should().EndWith(" Streak continues: 30.", "30 is a daily milestone, but an older row has no flag saying so");
    }

    /// <summary>
    /// A payload with no length, as a producer might send, still says something sensible rather than "0 days".
    /// </summary>
    [Fact(DisplayName = "P3-02: a milestone without a period length counts in periods")]
    public void Milestone_WithoutALength_CountsInPeriods()
    {
        const string json = """{"periodId":1,"completedCount":1,"totalCount":1,"streak":4,"outcome":1,"isMilestone":true}""";

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.Render(RoutinesNotificationTypes.RoutinePeriodEnded, json, AvailableLocales.En).Body
            .Should().EndWith(" Milestone: 4 periods in a row!");
    }
}
