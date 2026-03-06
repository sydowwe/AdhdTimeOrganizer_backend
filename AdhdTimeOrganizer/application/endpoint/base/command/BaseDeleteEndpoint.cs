using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseDeleteEndpoint<TEntity>(AppDbContext dbContext) : EndpointWithoutRequest
    where TEntity : class, IEntityWithId
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }

    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Delete(Route+"/{id:long}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Delete {entityName}";
            s.Description = $"Deletes a {entityName}";
            s.Response(204, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            dbContext.Set<TEntity>().Remove(entity);
            await dbContext.SaveChangesAsync(ct);

            await SendNoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }
}