using Quartz;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The Core.Scheduler implementation of <see cref="ICronEvaluator"/> over Quartz's <see cref="CronExpression"/>.
/// Stateless, so a singleton (one per process). It is pure expression math — no Quartz scheduler / trigger /
/// <see cref="ISchedulerFactory"/> dependency — so unlike <c>SchedulerService</c> it is safe under the
/// blanket DI scan even in hosts that never call <c>AddQuartz</c>.
/// <para>
/// Keeping the cron engine here means the Quartz package stays owned by the Scheduler module; the Reminders
/// module reasons about recurring-cron occurrences through the <see cref="ICronEvaluator"/> contract alone.
/// </para>
/// </summary>
public sealed class CronEvaluator : ICronEvaluator, ISingletonService
{
    public bool IsValid(string cron) => !string.IsNullOrWhiteSpace(cron) && CronExpression.IsValidExpression(cron);

    public DateTimeOffset? GetNextOccurrence(string cron, DateTimeOffset afterUtc) => new CronExpression(cron) { TimeZone = TimeZoneInfo.Utc }.GetTimeAfter(afterUtc);
}