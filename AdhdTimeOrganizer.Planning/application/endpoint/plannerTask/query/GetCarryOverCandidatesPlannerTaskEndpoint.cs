using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.query;

/// <summary>
/// <c>GET /planner-task/carry-over-candidates</c> — the tasks left open on the user's last
/// <see cref="LookbackDays"/> days, newest day first, for the day view's carry-over banner.
/// <para>
/// One read rather than the client fanning out a by-date + filter pair per day: the banner used to look at
/// yesterday alone for exactly that reason, so a task left open two days ago never surfaced again. Open means
/// <c>NotStarted</c> or <c>InProgress</c> and not background — a skip is a decision, not something left behind.
/// </para>
/// </summary>
public class GetCarryOverCandidatesPlannerTaskEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : EndpointWithoutRequest<List<CarryOverCandidateResponse>>
{
    /// <summary>How far back the banner looks. A week: past that, a task is a backlog item, not a slip.</summary>
    public const int LookbackDays = 7;

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/planner-task/carry-over-candidates");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Open planner tasks from the last week";
            s.Description = $"Not-started and in-progress, non-background tasks on the user's previous {LookbackDays} days "
                            + "(their own days, in their time zone), newest day first, each with its date.";
            s.Response<List<CarryOverCandidateResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var today = TaskPlannerHelper.TodayIn(await timeZones.GetAsync(userId, ct));
        var from = today.AddDays(-LookbackDays);

        // UserId by hand as well as through the host's global filter: this slice compiles against a plain
        // DbContext and cannot see that filter.
        var open = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Calendar.Date >= from && t.Calendar.Date < today
                        && !t.IsBackground
                        && (t.Status == PlannerTaskStatus.NotStarted || t.Status == PlannerTaskStatus.InProgress))
            .Select(t => new { t.Id, t.Calendar.Date })
            .ToListAsync(ct);

        if (open.Count == 0)
        {
            await Send.OkAsync([], ct);
            return;
        }

        var ids = open.Select(o => o.Id).ToList();
        var dates = open.ToDictionary(o => o.Id, o => o.Date);
        var tasks = await PlannerTaskResponse.Projection(
                dbContext.Set<PlannerTask>().AsNoTracking().Where(t => ids.Contains(t.Id)))
            .ToListAsync(ct);

        var candidates = tasks
            .Select(t => new CarryOverCandidateResponse { Date = dates[t.Id], Task = t })
            .OrderByDescending(c => c.Date)
            .ThenBy(c => c.Task.StartTime.Hours).ThenBy(c => c.Task.StartTime.Minutes)
            .ToList();

        await Send.OkAsync(candidates, ct);
    }
}
