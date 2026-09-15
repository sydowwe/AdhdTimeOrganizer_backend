using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.query;

public class GridActivityProjectProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityProjectProfile, ActivityProjectProfileResponse, ActivityProjectProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityProjectProfile is not IEntityWithUser, so the host's
    /// AppQueryFilters.ActivityProfileUserScope — not ApplyUserQueryFilters — is what backs this up, and
    /// this slice cannot see it. The owner is reached through the Activity either way.
    /// </summary>
    protected override Task<IQueryable<ActivityProjectProfile>> ApplyUserScoping(IQueryable<ActivityProjectProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityProjectProfile> ApplyCustomFiltering(IQueryable<ActivityProjectProfile> query,
        ActivityProjectProfileFilterRequest filter)
    {
        // ILIKE over ToContainsPattern, like the activity grid: Contains is case-sensitive in Postgres and
        // ILike does not escape % / _ on its own. See FreeTextFilterExtensions.
        if (!string.IsNullOrWhiteSpace(filter.ActivityName))
        {
            var pattern = filter.ActivityName.ToContainsPattern();
            query = query.Where(p => EF.Functions.ILike(p.Activity.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        // Any-of, and an empty list is "no opinion" — the combobox sends [] when every chip is cleared.
        if (filter.DifficultyLevels is { Length: > 0 })
            query = query.Where(p => filter.DifficultyLevels.Contains(p.DifficultyLevel));

        if (filter.ReadinessStatuses is { Length: > 0 })
            query = query.Where(p => filter.ReadinessStatuses.Contains(p.ReadinessStatus));

        if (filter.DifficultyLevel.HasValue)
            query = query.Where(p => p.DifficultyLevel == filter.DifficultyLevel.Value);

        if (filter.ReadinessStatus.HasValue)
            query = query.Where(p => p.ReadinessStatus == filter.ReadinessStatus.Value);

        if (filter.IsMessy.HasValue)
            query = query.Where(p => p.IsMessy == filter.IsMessy.Value);

        if (!string.IsNullOrWhiteSpace(filter.ProjectArea))
            query = query.Where(p => p.ProjectArea.Contains(filter.ProjectArea));

        return query;
    }
}