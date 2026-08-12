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


    public override void Configure()
    {
        Get($"/{EntityRoute}/form-select-options");

        Summary(s =>
        {
            s.Summary = $"Get {EntityRoute} form select options";
            s.Description = $"Retrieves all combinations of activity categories and roles from {EntityRoute} as select options";
            s.Response<List<ActivityFormSelectOptionsResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var query = GetBaseQuery(userId);

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