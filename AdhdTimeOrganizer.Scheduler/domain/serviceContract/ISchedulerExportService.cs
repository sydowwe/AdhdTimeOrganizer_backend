using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.application.export;
using AdhdTimeOrganizer.Scheduler.domain.@enum;

namespace AdhdTimeOrganizer.Scheduler.domain.serviceContract;

/// <summary>
/// Renders the dashboard projections (registered-jobs overview, run history) to a downloadable file
/// (XLSX or CSV). Pure formatting over the exact DTOs the JSON endpoints return, so the file can never drift
/// from the API. Generic infra columns only — <b>never the run payload snapshot</b> (it may carry PII; the
/// handler owns that boundary), so rows are labelled by the generic <c>JobKey</c>/<c>HandlerKey</c>/<c>OwnerModule</c>.
/// </summary>
public interface ISchedulerExportService
{
    ExportFile ExportJobsOverview(IReadOnlyList<ScheduledJobDto> jobs, SchedulerExportFormat format);

    ExportFile ExportRunHistory(IReadOnlyList<ScheduledJobRunDto> runs, SchedulerExportFormat format);
}