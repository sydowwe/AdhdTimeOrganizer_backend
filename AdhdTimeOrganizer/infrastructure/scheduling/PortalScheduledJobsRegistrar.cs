using AdhdTimeOrganizer.infrastructure.jobs;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.infrastructure.scheduling;

/// <summary>
/// Boot reconciliation for the portal's own recurring jobs — the same owner-pushes-its-schedule shape the
/// modules and slices use, so the host has no Quartz registration of its own either. All the host does for
/// scheduling now is call <c>services.AddSchedulerSubstrate()</c> and add these registrars.
/// <para>
/// <b>Required on every boot:</b> the registration is an idempotent upsert by <c>JobKey</c>, and the
/// Scheduler's RAM job store drops all triggers on restart.
/// </para>
/// </summary>
public sealed class PortalScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<PortalScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>
    /// Drains the dirty set <c>SuggestionPatternRefreshInterceptor</c> fills. Was a host-side Quartz trigger
    /// with a 10-second simple schedule; kept at 10 seconds via a seconds-field cron so suggestion freshness
    /// does not change.
    /// <para>
    /// <b>Retries and alerts are off for this one, deliberately.</b> At a 10-second cadence the next fire is
    /// always sooner than a backoff retry would be, and a transient refresh failure self-heals — retrying or
    /// alerting on it would only add noise. Note the cadence does mean roughly 8.6k
    /// <c>scheduled_job_run</c> rows a day; that ledger has its own retention purge, but it is the one cost
    /// of routing this job through the Scheduler.
    /// </para>
    /// </summary>
    public static RecurringJobRegistration SuggestionPatternRefreshRegistration { get; } = new()
    {
        JobKey = SuggestionPatternRefreshJobHandler.HandlerKey,
        HandlerKey = SuggestionPatternRefreshJobHandler.HandlerKey,
        OwnerModule = "Portal",
        Schedule = ScheduleSpec.FromCron("0/10 * * * * ?"),
        DisallowConcurrent = true,
        MaxRetries = 0,
        AlertOnFailure = false,
        Description = "Refreshes the three suggestion-pattern materialized views marked dirty since the last run."
    };

    /// <summary>
    /// The morning briefing. Every 15 minutes in UTC; the handler is zone-aware per user and decides each user's
    /// morning once, so the cadence only bounds how late after wake-up the briefing can arrive.
    /// </summary>
    public static RecurringJobRegistration MorningBriefingRegistration { get; } = new()
    {
        JobKey = MorningBriefingJobHandler.HandlerKey,
        HandlerKey = MorningBriefingJobHandler.HandlerKey,
        OwnerModule = "Portal",
        Schedule = ScheduleSpec.FromCron("0 0/15 * * * ?"),
        DisallowConcurrent = true,
        Description = "Sends each user their morning briefing — today's plan, carry-overs, due and overdue to-dos, routines — once their own wake-up has come."
    };

    /// <summary>
    /// Unconfirmed sign-ups: one confirmation reminder a day after sign-up, deletion after two weeks. Hourly, at minute 7
    /// to stay off the quarter-hour sweeps; the windows are a day and two weeks, so the cadence only bounds how late in
    /// its hour each step lands.
    /// </summary>
    public static RecurringJobRegistration UnconfirmedAccountsRegistration { get; } = new()
    {
        JobKey = UnconfirmedAccountJobHandler.HandlerKey,
        HandlerKey = UnconfirmedAccountJobHandler.HandlerKey,
        OwnerModule = "Portal",
        Schedule = ScheduleSpec.FromCron("0 7 * * * ?"),
        DisallowConcurrent = true,
        Description = "Resends the confirmation link once to a sign-up still unconfirmed after a day, and deletes it after two weeks."
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

        foreach (var registration in new[] { SuggestionPatternRefreshRegistration, MorningBriefingRegistration, UnconfirmedAccountsRegistration })
        {
            try
            {
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't block host startup on a scheduling problem — the job simply does not run and shows as
                // absent in the dashboard. One failure does not cost the other job its registration. JobKey is non-PII.
                logger.LogError(ex, "Failed to register the portal recurring job {JobKey}", registration.JobKey);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
