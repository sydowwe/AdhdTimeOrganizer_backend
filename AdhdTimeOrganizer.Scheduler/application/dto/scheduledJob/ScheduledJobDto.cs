using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;

/// <summary>Registry overview row for the dashboard — one per registered job.</summary>
public record ScheduledJobDto : IdResponse, IProjectionResponse<ScheduledJobDto, ScheduledJob>
{
    public required string JobKey { get; init; }
    public required string HandlerKey { get; init; }
    public required string OwnerModule { get; init; }
    public string? Description { get; init; }

    public required JobScheduleType ScheduleType { get; init; }
    public string? Cron { get; init; }
    public JobIntervalPreset? IntervalPreset { get; init; }
    public int? IntervalCount { get; init; }
    public DateTime? RunAtUtc { get; init; }
    public string? TimeZoneId { get; init; }
    public required MisfirePolicy MisfirePolicy { get; init; }
    public required bool DisallowConcurrent { get; init; }
    public required int MaxRetries { get; init; }
    public required bool AlertOnFailure { get; init; }

    public required JobStatus Status { get; init; }
    public DateTime? NextRunAt { get; init; }
    public DateTime? LastRunAt { get; init; }
    public RunOutcome? LastOutcome { get; init; }
    public required bool IsActive { get; init; }

    public static IQueryable<ScheduledJobDto> Projection(IQueryable<ScheduledJob> query)
    {
        return query.Select(x => new ScheduledJobDto
        {
            Id = x.Id,
            JobKey = x.JobKey,
            HandlerKey = x.HandlerKey,
            OwnerModule = x.OwnerModule,
            Description = x.Description,
            ScheduleType = x.ScheduleType,
            Cron = x.Cron,
            IntervalPreset = x.IntervalPreset,
            IntervalCount = x.IntervalCount,
            RunAtUtc = x.RunAtUtc,
            TimeZoneId = x.TimeZoneId,
            MisfirePolicy = x.MisfirePolicy,
            DisallowConcurrent = x.DisallowConcurrent,
            MaxRetries = x.MaxRetries,
            AlertOnFailure = x.AlertOnFailure,
            Status = x.Status,
            NextRunAt = x.NextRunAt,
            LastRunAt = x.LastRunAt,
            LastOutcome = x.LastOutcome,
            IsActive = x.IsActive
        });
    }
}