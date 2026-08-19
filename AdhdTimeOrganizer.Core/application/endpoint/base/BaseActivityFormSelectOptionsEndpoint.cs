using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Core.application.endpoint.@base.read;

public abstract class BaseActivityFormSelectOptionsEndpoint<TEntity>(DbContext dbContext) : EndpointWithoutRequest<List<ActivityFormSelectOptionsResponse>>
    where TEntity : class
{
    // Plain DbContext, not the host's AppDbContext: this base lives in AdhdTimeOrganizer.Core, which
    // cannot reference the host. ModuleServiceExtensions aliases DbContext -> AppDbContext, so what
    // subclasses actually get is still the app context, global query filters and all.
    protected readonly DbContext DbContext = dbContext;

    public abstract string EntityRoute { get; }

    /// <summary>
    /// The query-string name that opts an archived activity back into the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default <c>false</c>, so every call site that existed before A9 keeps its current behaviour and
    /// the record-creating forms need no change at all. This is the one place where "only pickers
    /// exclude archived activities" cuts against itself, and the parameter is the resolution.
    /// </para>
    /// <para>
    /// <c>/activity-history/form-select-options</c> is the case that forced it: it does not feed a form
    /// for creating a record, it feeds <c>HistoryPanelFilter</c> — the filter over history. Excluding
    /// archived activities there would mean archiving an activity silently removes the user's ability to
    /// filter their own history by it, while the records stay visible and keep showing its name. Losing
    /// a filter you can still see the rows of is a bad trade, so filter surfaces pass
    /// <c>?includeArchived=true</c> and creation surfaces do not.
    /// </para>
    /// </remarks>
    private const string IncludeArchivedQueryParam = "includeArchived";

    public override void Configure()
    {
        Get($"/{EntityRoute}/form-select-options");

        Summary(s =>
        {
            s.Summary = $"Get {EntityRoute} form select options";
            s.Description = $"Retrieves all combinations of activity categories and roles from {EntityRoute} as select options. "
                            + "Archived activities are excluded unless ?includeArchived=true is passed — pass it from filter "
                            + "surfaces, never from forms that create a record.";
            s.Response<List<ActivityFormSelectOptionsResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var includeArchived = Query<bool?>(IncludeArchivedQueryParam, isRequired: false) ?? false;

        var query = GetBaseQuery(userId);
        if (!includeArchived)
            query = query.Where(a => !a.IsArchived);

        var activities = await query
            .Include(a => a.Category)
            .Include(a => a.Role)
            .Select(a => new
            {
                ActivityId = a.Id,
                ActivityName = a.Name,
                CategoryId = a.CategoryId,
                CategoryName = a.Category != null ? a.Category.Name : null,
                RoleId = a.RoleId,
                RoleName = a.Role.Name
            })
            .Distinct()
            .ToListAsync(ct);

        var options = activities
            .Select(a => new ActivityFormSelectOptionsResponse
            {
                Id = a.ActivityId,
                Text = a.ActivityName,
                RoleOption = new SelectOptionResponse(a.RoleId, a.RoleName),
                CategoryOption = a.CategoryId.HasValue && a.CategoryName != null
                    ? new SelectOptionResponse(a.CategoryId.Value, a.CategoryName)
                    : null,
                TaskPriorityOption = null,
                RoutineTimePeriodOption = null
            })
            .ToList();

        await Send.OkAsync(options, ct);
    }

    protected abstract IQueryable<Activity> GetBaseQuery(long userId);
}
