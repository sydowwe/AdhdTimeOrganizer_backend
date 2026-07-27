using AdhdTimeOrganizer.Scheduler.domain.@enum;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;

/// <summary>
/// Filter for the append-only run-history view: by job (id or key snapshot), owner module, outcome,
/// trigger source, and a <c>StartedAt</c> date range.
/// </summary>
public record JobRunHistoryFilterRequest : IFilterRequest
{
    public long? ScheduledJobId { get; set; }
    public string? JobKey { get; set; }
    public string? OwnerModule { get; set; }
    public RunOutcome? Outcome { get; set; }
    public TriggerSource? TriggerSource { get; set; }
    public DateTime? StartedFrom { get; set; }
    public DateTime? StartedTo { get; set; }
}