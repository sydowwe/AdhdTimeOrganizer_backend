using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GridActivityBacklogProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse, ActivityBacklogProfileFilterRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    public override string EndpointPath => "grid";

    /// <summary>
    /// The caller's anchors. Scoped by hand for the same reason the profiles are: MemoryAnchor does carry a
    /// global query filter, but this subquery decides a field the client treats as "done", and it is one
    /// line to stop depending on a filter configured in another project.
    /// </summary>
    private IQueryable<MemoryAnchor> ScopedAnchors() => _dbContext.Set<MemoryAnchor>().Where(m => m.UserId == User.GetId());

    /// <summary>
    /// Overridden so IsAnchored / MemoryAnchorId are computed in SQL rather than overlaid afterwards: the
    /// base sorts the projected queryable, so a field filled in by PostProcessItems would sort on false.
    /// </summary>
    protected override Func<IQueryable<ActivityBacklogProfile>, IQueryable<ActivityBacklogProfileResponse>> Projection =>
        query => ActivityBacklogProfileResponse.ProjectionWithAnchors(query, ScopedAnchors());

    /// <summary>
    /// The one field that cannot be computed in SQL: the label inference has no translation. So it does sort
    /// as its pre-fill value — the stored code, null for every user-created row — exactly as the note on
    /// <see cref="Projection"/> warns. That is harmless here, because nothing sorts or filters on
    /// <c>weatherDependencyCode</c>; the grid's weather filter and sort go through the lookup id and text.
    /// Don't "fix" this by moving it into the projection.
    /// </summary>
    protected override List<ActivityBacklogProfileResponse> PostProcessItems(List<ActivityBacklogProfileResponse> items) =>
        items.Select(item => item.WithEffectiveWeatherDependencyCode()).ToList();

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityBacklogProfile is not IEntityWithUser, so the host's
    /// AppQueryFilters.ActivityProfileUserScope — not ApplyUserQueryFilters — is what backs this up, and
    /// this slice cannot see it. The owner is reached through the Activity either way.
    /// </summary>
    protected override Task<IQueryable<ActivityBacklogProfile>> ApplyUserScoping(IQueryable<ActivityBacklogProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityBacklogProfile> ApplyCustomFiltering(IQueryable<ActivityBacklogProfile> query,
        ActivityBacklogProfileFilterRequest filter)
    {
        // ILIKE over ToContainsPattern, like the activity grid: Contains is case-sensitive in Postgres and
        // ILike does not escape % / _ on its own. See FreeTextFilterExtensions.
        if (!string.IsNullOrWhiteSpace(filter.ActivityName))
        {
            var pattern = filter.ActivityName.ToContainsPattern();
            query = query.Where(p => EF.Functions.ILike(p.Activity.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        // Any-of on every list, and an empty list is "no opinion" — the combobox sends [] when every chip
        // is cleared.
        if (filter.LocationTypeIds is { Length: > 0 })
            query = query.Where(p => filter.LocationTypeIds.Contains(p.LocationTypeId));

        if (filter.WeatherDependencyIds is { Length: > 0 })
            query = query.Where(p => filter.WeatherDependencyIds.Contains(p.WeatherDependencyId));

        if (filter.ExpectedCostTierIds is { Length: > 0 })
            query = query.Where(p => filter.ExpectedCostTierIds.Contains(p.ExpectedCostTierId));

        if (filter.EnergyLevels is { Length: > 0 })
            query = query.Where(p => filter.EnergyLevels.Contains(p.EnergyLevel));

        if (filter.EffortTypes is { Length: > 0 })
        {
            // The column is EffortType?, and Npgsql's array converter refuses a non-nullable element type
            // against it (a 500, not a translation failure), so the parameter is widened to match.
            var effortTypes = filter.EffortTypes.Select(e => (EffortType?)e).ToArray();
            query = query.Where(p => p.EffortType != null && effortTypes.Contains(p.EffortType));
        }

        if (filter.MaxDurationMinutes.HasValue)
            query = query.Where(p => p.DurationMinutes <= filter.MaxDurationMinutes.Value);

        if (filter.MinParticipants.HasValue)
            query = query.Where(p => p.MinParticipants >= filter.MinParticipants.Value);

        // A null MaxParticipants on the row is "any number", which never fits under a cap.
        if (filter.MaxParticipants.HasValue)
            query = query.Where(p => p.MaxParticipants != null && p.MaxParticipants.Value <= filter.MaxParticipants.Value);

        if (filter.EnergyLevel.HasValue)
            query = query.Where(p => p.EnergyLevel == filter.EnergyLevel.Value);

        if (filter.EffortType.HasValue)
            query = query.Where(p => p.EffortType == filter.EffortType.Value);

        if (filter.IsRepeatable.HasValue)
            query = query.Where(p => p.IsRepeatable == filter.IsRepeatable.Value);

        if (filter.IsAnchored.HasValue)
        {
            var anchors = ScopedAnchors();
            // Mirrors ProjectionWithAnchors: a repeatable entry is never anchored, so it belongs on the
            // false side whatever its activity's anchors say.
            query = filter.IsAnchored.Value
                ? query.Where(p => !p.IsRepeatable && anchors.Any(m => m.ActivityId == p.ActivityId))
                : query.Where(p => p.IsRepeatable || !anchors.Any(m => m.ActivityId == p.ActivityId));
        }

        return query;
    }
}