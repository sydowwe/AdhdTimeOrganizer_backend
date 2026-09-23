using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GetAllActivityBacklogProfileEndpoint(DbContext dbContext) : BaseGetAllEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse>(dbContext)
{
    /// <summary>
    /// Ownership. Unlike <c>BaseGridEndpoint</c>, <c>BaseGetAllEndpoint</c> has no
    /// <c>ApplyUserScoping</c> hook — its <c>Filter</c> is the identity function and the handler reads
    /// <c>Set&lt;TEntity&gt;()</c> straight, so without this override the route answered 200 with every
    /// user's backlog profiles. The owner is reached through the Activity, the same way the three grids
    /// do it.
    ///
    /// <para>Belt and braces on purpose: <c>AppQueryFilters.ActivityProfileUserScope</c> in the host's
    /// <c>AppDbContext</c> now scopes these rows globally too. This slice cannot see that filter, so it
    /// does not depend on it.</para>
    /// </summary>
    protected override IQueryable<ActivityBacklogProfile> Filter(IQueryable<ActivityBacklogProfile> query) =>
        query.Where(p => p.Activity.UserId == User.GetId());

    /// <summary>
    /// See <see cref="ActivityBacklogProfileResponse.WeatherDependencyCode"/> — the label inference runs after
    /// the load, on every route that serves this DTO.
    /// </summary>
    protected override List<ActivityBacklogProfileResponse> PostProcessItems(List<ActivityBacklogProfileResponse> items) =>
        items.Select(item => item.WithEffectiveWeatherDependencyCode()).ToList();
}