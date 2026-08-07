using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;

/// <summary>A single execution from the append-only run log.</summary>
public record ScheduledJobRunDto : IdResponse, IProjectionResponse<ScheduledJobRunDto, ScheduledJobRun>
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
    public long? ReplaysRunId { get; init; }

    public static IQueryable<ScheduledJobRunDto> Projection(IQueryable<ScheduledJobRun> query)
    {
        return query.Select(x => new ScheduledJobRunDto
        {
            Id = x.Id,
            ScheduledJobId = x.ScheduledJobId,
            JobKeySnapshot = x.JobKeySnapshot,
            HandlerKeySnapshot = x.HandlerKeySnapshot,
            ScheduledFireTime = x.ScheduledFireTime,
            StartedAt = x.StartedAt,
            FinishedAt = x.FinishedAt,
            DurationMs = x.DurationMs,
            Outcome = x.Outcome,
            ErrorMessage = x.ErrorMessage,
            ErrorType = x.ErrorType,
            TriggerSource = x.TriggerSource,
            CorrelationId = x.CorrelationId,
            ReplaysRunId = x.ReplaysRunId
        });
    }
}