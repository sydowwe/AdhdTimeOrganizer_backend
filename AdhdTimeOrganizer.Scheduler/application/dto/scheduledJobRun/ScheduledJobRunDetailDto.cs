using AdhdTimeOrganizer.Scheduler.domain.@enum;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;

/// <summary>
/// Single-run detail: the full run row plus its payload snapshot and both directions of replay lineage —
/// the run it replays (<see cref="ReplaysRunId"/>) and the runs that replay it (<see cref="ReplayedByRunIds"/>).
/// Unlike the list/export surfaces, this Admin-only by-id view <b>does</b> surface the payload snapshot for
/// inspection; payload PII hygiene is the handler's responsibility (see <c>ScheduledJobRun</c>).
/// </summary>
public record ScheduledJobRunDetailDto : IdResponse
{
    public required long ScheduledJobId { get; init; }
    public required string JobKeySnapshot { get; init; }
    public required string HandlerKeySnapshot { get; init; }

    public DateTime? ScheduledFireTime { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public int? DurationMs { get; init; }

    public required RunOutcome Outcome { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public required TriggerSource TriggerSource { get; init; }
    public required string CorrelationId { get; init; }

    public string? PayloadSnapshotJson { get; init; }

    /// <summary>The run this row re-ran (replay lineage), if any.</summary>
    public long? ReplaysRunId { get; init; }

    /// <summary>The runs that re-ran this one — the reverse of <see cref="ReplaysRunId"/>.</summary>
    public required List<long> ReplayedByRunIds { get; init; }
}