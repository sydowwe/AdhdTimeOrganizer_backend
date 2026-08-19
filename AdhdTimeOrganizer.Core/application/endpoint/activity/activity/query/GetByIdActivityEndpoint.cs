using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

public class GetByIdActivityEndpoint(
    DbContext dbContext,
    IActivityReferenceService referenceService)
    : BaseGetByIdEndpoint<Activity, ActivityResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Fills in <c>usageCount</c> / <c>canDelete</c>, which the static projection cannot reach.
    /// </summary>
    /// <remarks>
    /// A second round trip rather than the grid's correlated subquery, because there is nothing to sort
    /// on one row and a <c>GROUP BY</c> over a single id is the cheaper shape. This endpoint stays
    /// unaffected by archiving in every other respect — it returns archived activities normally, which
    /// is what lets the edit form and the merge dialog open one.
    /// </remarks>
    protected override async Task<ActivityResponse> PostProcessAsync(ActivityResponse entity, CancellationToken ct)
    {
        var counts = await referenceService.CountByActivityAsync(dbContext, [entity.Id], ct);
        var usageCount = counts.GetValueOrDefault(entity.Id);

        return entity with { UsageCount = usageCount, CanDelete = usageCount == 0 };
    }
}
