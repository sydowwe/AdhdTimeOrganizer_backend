using FluentAssertions;
using Quartz;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Scheduler.infrastructure;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the correlation-id handle the trigger-now / replay endpoints hand back. Those endpoints cannot return
/// a run id — the run log is append-only and its row is inserted at completion — so they mint a correlation id,
/// carry it to the dispatcher on the Quartz <see cref="JobDataMap"/>, and the dispatcher stamps it on the run
/// row. That hop is by string key through an untyped map: if the dispatcher stopped honouring the supplied id
/// it would silently mint its own, the endpoint would still return a well-formed id, and every client lookup
/// filtered by it would just find nothing — no exception, no log line, no failing route test.
/// </summary>
public class SchedulerCorrelationIdTests
{
    [Fact]
    public void Dispatcher_honours_the_correlation_id_the_caller_enqueued_with()
    {
        var minted = SchedulerQuartzConfig.NewCorrelationId();
        var data = new JobDataMap
        {
            { SchedulerQuartzConfig.JobKeyDataKey, "some.job" },
            { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Manual) },
            { SchedulerQuartzConfig.CorrelationIdDataKey, minted }
        };

        SchedulerQuartzConfig.ParseCorrelationId(data).Should().Be(minted);
    }

    [Fact]
    public void A_minted_correlation_id_fits_the_run_rows_column()
    {
        // correlation_id is varchar(100) — see ScheduledJobRunEntityConfiguration. A longer id would fail the
        // insert inside the dispatcher, i.e. after the endpoint had already returned 200 with that id.
        SchedulerQuartzConfig.NewCorrelationId().Should().HaveLength(32);
    }

    [Fact]
    public void A_scheduled_fire_carries_no_caller_id_and_still_gets_one()
    {
        var data = new JobDataMap { { SchedulerQuartzConfig.JobKeyDataKey, "some.job" } };

        SchedulerQuartzConfig.ParseCorrelationId(data).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_auto_retry_does_not_inherit_the_original_fires_correlation_id()
    {
        // A retry is its own fire and gets its own id, so filtering history by the id trigger-now returned
        // yields exactly one row rather than the whole retry chain. Lineage is ReplaysRunId's job.
        var retryData = SchedulerQuartzConfig.BuildRetryDataMap("some.job", 1, 3, lineageRunId: 42, payloadJson: null);

        retryData.ContainsKey(SchedulerQuartzConfig.CorrelationIdDataKey).Should().BeFalse();
    }
}
