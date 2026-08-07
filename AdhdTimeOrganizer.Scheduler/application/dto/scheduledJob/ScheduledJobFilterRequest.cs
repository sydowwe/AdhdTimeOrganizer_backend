using AdhdTimeOrganizer.Scheduler.domain.@enum;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;

/// <summary>Filter for the registered-jobs grid: by owner module, handler key, status, schedule type, next-run range.</summary>
public record ScheduledJobFilterRequest : IFilterRequest
{
    public string? OwnerModule { get; set; }
    public string? HandlerKey { get; set; }
    public JobStatus? Status { get; set; }
    public JobScheduleType? ScheduleType { get; set; }
    public DateTime? NextRunFrom { get; set; }
    public DateTime? NextRunTo { get; set; }
}