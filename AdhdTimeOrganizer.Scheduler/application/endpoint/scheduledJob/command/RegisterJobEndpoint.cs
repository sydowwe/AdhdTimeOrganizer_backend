using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.command;

/// <summary>
/// Diagnostic / manual-ops register (idempotent upsert by JobKey). Owners normally register from their own
/// startup hosted service against <see cref="IScheduler"/> directly â€” this endpoint is for ad-hoc admin use.
/// Open to any signed-in user (User/Admin/Root).
/// </summary>
public class RegisterJobEndpoint(IScheduler scheduler) : Endpoint<RegisterJobRequest>
{
    public override void Configure()
    {
        Post("/scheduled-job/register");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Register (idempotent upsert) a recurring job by JobKey";
            s.Response(204, "Registered");
            s.Response(400, "Invalid schedule or unknown handler key");
        });
    }

    public override async Task HandleAsync(RegisterJobRequest req, CancellationToken ct)
    {
        if (req.ScheduleType == JobScheduleType.Interval && req.IntervalPreset is null)
        {
            AddError(r => r.IntervalPreset, "An interval schedule requires an interval preset.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        try
        {
            await scheduler.RegisterRecurringJobAsync(req.ToRegistration(), ct);
        }
        catch (ArgumentException ex)
        {
            AddError(ex.Message);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}