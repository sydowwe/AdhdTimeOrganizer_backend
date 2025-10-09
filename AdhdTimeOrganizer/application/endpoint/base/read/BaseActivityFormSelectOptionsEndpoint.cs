using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseActivityFormSelectOptionsEndpoint<TEntity>(AppCommandDbContext appDbContext) : EndpointWithoutRequest<List<ActivityFormSelectOptionsResponse>>
    where TEntity : class
{
    protected readonly AppCommandDbContext _appDbContext = appDbContext;

    public abstract string EntityRoute { get; }
    
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        Get($"/{EntityRoute}/form-select-options");
        Roles(AllowedRoles());
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

        await SendOkAsync(options, ct);
    }

    protected abstract IQueryable<Activity> GetBaseQuery(long userId);
}
