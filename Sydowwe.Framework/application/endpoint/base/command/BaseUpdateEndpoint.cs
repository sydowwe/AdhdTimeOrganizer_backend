using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command;

/// <summary>
/// Standard full-update endpoint (PUT /{id}).
///
/// Concurrency scope: the <c>row_version</c> token prevents two overlapping
/// in-flight requests from overwriting each other (load-then-save race). It does
/// NOT protect against the stale-form case — a user who opens a form, another
/// admin edits the record, and the first user submits later will silently win.
/// If an in-flight conflict is detected, EF throws <see cref="DbUpdateConcurrencyException"/>
/// which is caught and returned as HTTP 409.
///
/// For entities that genuinely need stale-form protection implement
/// <see cref="IUpdateRequestWithRowVersion{TEntity}"/> on the request DTO — the
/// handler will set the client-supplied token as the EF original value before
/// saving, so a stale submit also produces a 409.
/// </summary>
public abstract class BaseUpdateEndpoint<TEntity, TRequest>(DbContext dbContext) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithId
    where TRequest : class, IUpdateRequest<TEntity>
{
    /// <summary>
    /// The ambient context, so a subclass hook can read tracked/original values (e.g. to compare a mapped
    /// field against the value as loaded) without re-capturing the constructor parameter.
    /// </summary>
    protected DbContext DbContext => dbContext;

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Put(Route + "/{id:long}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Update {entityName}";
            s.Description = $"Updates an existing {entityName}";
            s.Response(200, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            if (!await BeforeMapping(req, ct))
                return;

            var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!await AuthorizeAsync(entity, ct))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (req is IUpdateRequestWithRowVersion<TEntity> rvReq)
                dbContext.Entry(entity).Property("row_version").OriginalValue = rvReq.RowVersion;

            await UpdateEntityAsync(entity, req, ct);

            if (!await AfterMapping(entity, req, ct))
                return;

            dbContext.Set<TEntity>().Update(entity);
            await dbContext.SaveChangesAsync(ct);

            await AfterSave(entity, ct);
            await Send.OkAsync(entity.Id, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    protected virtual Task<bool> BeforeMapping(TRequest req, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>
    /// Post-fetch ownership/authorization check on the loaded entity (e.g. IDOR / "is this row mine?").
    /// Return <c>false</c> to respond 403. Default allows everyone the role check already let through.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);

    protected virtual Task UpdateEntityAsync(TEntity entity, TRequest req, CancellationToken ct = default)
    {
        req.UpdateEntity(entity);
        return Task.CompletedTask;
    }

    protected virtual Task<bool> AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default) => Task.FromResult(true);

    protected virtual Task AfterSave(TEntity entity, CancellationToken ct = default) => Task.CompletedTask;
}