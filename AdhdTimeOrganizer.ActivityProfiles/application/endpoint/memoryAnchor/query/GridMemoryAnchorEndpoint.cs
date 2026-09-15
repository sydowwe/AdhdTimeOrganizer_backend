using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GridMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGridEndpoint<MemoryAnchor, MemoryAnchorResponse, MemoryAnchorFilterRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// The one concern this grid lifts, on every set it reads. A <see cref="MemoryAnchor"/> deliberately
    /// carries no archived filter of its own — an anchor is a record of something already done, and
    /// archiving the activity afterwards is the normal case, not an edge one — but <c>Activity</c> does,
    /// and this response projects the activity through a required navigation.
    /// </summary>
    /// <remarks>
    /// Left in place, the filter turned that navigation into an inner join that dropped the row from the
    /// page while <c>GetGridDataAsync</c> still counted it off the unprojected query: a grid reporting
    /// <c>itemsCount: 1</c> with <c>items: []</c>, on no page the client can turn to. Not an exception,
    /// not a log line — the shape of the projection alone decided it, which is why
    /// <c>ArchivedActivityProfileReadTests</c> asserts on the row rather than on the status code.
    /// Never <see cref="QueryFilters.UserScope"/> here, and never the argument-less overload: the two
    /// Source subqueries below are scoped by hand, and the anchors by the global user filter.
    /// </remarks>
    private static readonly string[] LiftArchived = [AppQueryFilters.ActivityArchived];

    /// <summary>
    /// The caller's profiles, behind the Source column. Scoped by hand through the Activity because neither
    /// profile is <c>IEntityWithUser</c> — the host's <c>AppQueryFilters.ActivityProfileUserScope</c> is
    /// what backs that up, and this slice cannot see it.
    /// </summary>
    /// <remarks>
    /// Lifts the archived concern for the same reason the anchors do, and it has to be lifted here too:
    /// both profiles mirror <c>!Activity.IsArchived</c> onto themselves, so an anchor of an archived
    /// activity would otherwise come back with a Source column reading "from nowhere" — the profiles it
    /// came from hidden by the filter rather than absent.
    /// </remarks>
    private IQueryable<ActivityBucketListProfile> ScopedBucketLists() =>
        _dbContext.Set<ActivityBucketListProfile>()
            .IgnoreNamedQueryFilters(LiftArchived)
            .Where(p => p.Activity.UserId == User.GetId());

    /// <inheritdoc cref="ScopedBucketLists"/>
    private IQueryable<ActivityBacklogProfile> ScopedBacklogs() =>
        _dbContext.Set<ActivityBacklogProfile>()
            .IgnoreNamedQueryFilters(LiftArchived)
            .Where(p => p.Activity.UserId == User.GetId());

    /// <summary>
    /// Lifts <see cref="LiftArchived"/> on the anchor set itself, which is what makes the count and the page
    /// agree: it runs before <c>GetGridDataAsync</c> splits them, so both see the same rows.
    /// </summary>
    protected override IQueryable<MemoryAnchor> ApplyBaseFiltering(IQueryable<MemoryAnchor> query) =>
        query.IgnoreNamedQueryFilters(LiftArchived);

    /// <summary>
    /// Overridden so the three Source fields are computed in SQL rather than overlaid afterwards: the base
    /// sorts the projected queryable, so a field filled in by PostProcessItems would sort on false.
    /// </summary>
    protected override Func<IQueryable<MemoryAnchor>, IQueryable<MemoryAnchorResponse>> Projection =>
        query => MemoryAnchorResponse.ProjectionWithProfiles(query, ScopedBucketLists(), ScopedBacklogs());

    /// <summary>
    /// The grid shows one <c>periodKey</c> column and the response stores the period as two members, so the
    /// key resolves nothing and <c>SortByMany</c> throws. Expanded into year-then-month rather than given a
    /// computed <c>year * 100 + month</c> proxy on the response: the two members are a prefix of
    /// <c>MemoryAnchorConfiguration</c>'s <c>(ActivityId, AnchorYear, AnchorMonth)</c> index, so a grid
    /// filtered to one activity — which the filter offers — orders straight off it, while the arithmetic
    /// form would need an expression index to do the same.
    /// </summary>
    /// <remarks>
    /// Both members inherit the column's single <c>isDesc</c>, so descending is
    /// <c>anchor_year DESC, anchor_month DESC</c> — a period is one thing to sort, not two that could
    /// disagree. Year leads because <c>SortByMany</c> appends each key with <c>ThenBy</c>.
    /// </remarks>
    protected override SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) =>
        sortBy.ExpandSortKey("periodKey", nameof(MemoryAnchorResponse.AnchorYear), nameof(MemoryAnchorResponse.AnchorMonth));

    protected override IQueryable<MemoryAnchor> ApplyCustomFiltering(IQueryable<MemoryAnchor> query, MemoryAnchorFilterRequest filter)
    {
        // ILIKE over ToContainsPattern, like the activity grid: Contains is case-sensitive in Postgres and
        // ILike does not escape % / _ on its own. See FreeTextFilterExtensions. The navigation is safe to
        // walk here: ApplyBaseFiltering already lifted the archived filter that would otherwise turn it
        // into a row-dropping inner join.
        if (!string.IsNullOrWhiteSpace(filter.ActivityName))
        {
            var pattern = filter.ActivityName.ToContainsPattern();
            query = query.Where(m => EF.Functions.ILike(m.Activity.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (filter.MinRating.HasValue)
            query = query.Where(m => m.Rating >= filter.MinRating.Value);

        if (filter.AnchorMonth.HasValue)
            query = query.Where(m => m.AnchorMonth == filter.AnchorMonth.Value);

        if (filter.AnchorYear.HasValue)
            query = query.Where(m => m.AnchorYear == filter.AnchorYear.Value);

        if (filter.ActivityId.HasValue)
            query = query.Where(m => m.ActivityId == filter.ActivityId.Value);

        return query;
    }
}