using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <summary>
/// A backlog row as the grid renders it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the activity is nested rather than flattened.</b> This carried a flat <c>ActivityName</c>, which
/// meant the grid's <c>activity.name</c> sort key resolved to nothing and had to be remapped, and the row
/// could render a name but neither the category subtitle nor the avatar. Deriving from
/// <see cref="WithActivityResponse"/> puts the whole <see cref="ActivityResponse"/> on the row — the same
/// shape history rows, to-do items, planner tasks and timer presets already carry — so
/// <c>activity.name</c>, <c>activity.role.name</c> and <c>activity.category.name</c> all resolve natively.
/// </para>
/// <para>
/// The nested initializer is written out rather than shared because an EF projection cannot call into a
/// stored expression without an expression-expanding query provider, which this solution does not use.
/// Every other nested-activity response in the portal repeats it for the same reason.
/// </para>
/// <para>
/// <see cref="ActivityId"/> stays alongside it. It duplicates <c>Activity.Id</c>, but it is what the
/// create/update forms and the temptation-bundling pairing UI post back, and reading it off a nested
/// object is a change to every caller for nothing.
/// </para>
/// </remarks>
public record ActivityBacklogProfileResponse : WithActivityResponse, IProjectionResponse<ActivityBacklogProfileResponse, ActivityBacklogProfile>
{
    public required long ActivityId { get; init; }

    public required long LocationTypeId { get; init; }

    /// <summary>
    /// The lookup itself, so the grid can render a label instead of an id and sort on
    /// <c>locationType.text</c>. The <c>…Id</c> above is kept for the forms that post it back.
    /// </summary>
    public required SelectOptionResponse LocationType { get; init; }

    public required long WeatherDependencyId { get; init; }

    /// <inheritdoc cref="LocationType"/>
    public required SelectOptionResponse WeatherDependency { get; init; }

    /// <summary>
    /// The <see cref="WeatherDependencyCodes.Effective"/> value of <see cref="WeatherDependency"/>: the row's
    /// stored <c>Code</c>, else <see cref="WeatherDependencyCodes.Infer"/> of its text, else null. Null means
    /// "unrecognised row", and the client treats it as no weather opinion for this entry — as it does
    /// <see cref="WeatherDependencyCodes.None"/>, which is why this has to be on the row at all: the
    /// weather-fit set always contains <c>none</c>, so ids alone cannot tell "fits any weather" from "fits
    /// today's".
    ///
    /// <para><b>Two-stage on purpose.</b> The projections carry only the stored column, because
    /// <see cref="WeatherDependencyCodes.Infer"/> has no SQL translation; every endpoint serving this DTO then
    /// finishes it with <see cref="WithEffectiveWeatherDependencyCode"/> after the rows are loaded. Optional
    /// rather than <c>required</c> so the other responses that point at this one need not change.</para>
    /// </summary>
    public string? WeatherDependencyCode { get; init; }

    public required EnergyLevel EnergyLevel { get; init; }
    public EffortType? EffortType { get; init; }
    public required int MinParticipants { get; init; }
    public int? MaxParticipants { get; init; }

    public required long ExpectedCostTierId { get; init; }

    /// <inheritdoc cref="LocationType"/>
    public required SelectOptionResponse ExpectedCostTier { get; init; }

    public required int DurationMinutes { get; init; }

    /// <summary>
    /// The activity can be done again. One name, one direction, everywhere — entity, column, request,
    /// response and filter all say <c>IsRepeatable</c>, and nothing in the solution exposes the inverse
    /// under a second name. A client that wants "is one-time" negates it at the point of use.
    /// </summary>
    public required bool IsRepeatable { get; init; }

    /// <summary>
    /// True when the user has recorded a <see cref="MemoryAnchor"/> against this profile's Activity AND the
    /// entry is not repeatable — anchoring marks an experience as had, which only closes a
    /// backlog entry that was meant to happen once. A repeatable entry is never "done", so this stays false
    /// for it however many anchors its activity carries. Derived on read, never stored.
    /// </summary>
    public bool IsAnchored { get; init; }

    /// <summary>
    /// The anchor behind <see cref="IsAnchored"/> — the most recent one by (year, month, id) when the
    /// activity has several. Null whenever <see cref="IsAnchored"/> is false, repeatable entries included.
    /// </summary>
    public long? MemoryAnchorId { get; init; }

    /// <summary>
    /// Leaves <see cref="IsAnchored"/>/<see cref="MemoryAnchorId"/> at their defaults: the anchors are not
    /// reachable by navigation from the profile (Activity.MemoryAnchors was deleted in the slice
    /// extraction and must stay deleted), and this signature has no DbContext to reach them with.
    /// Callers that need those two fields use <see cref="ProjectionWithAnchors"/>.
    /// </summary>
    public static IQueryable<ActivityBacklogProfileResponse> Projection(IQueryable<ActivityBacklogProfile> query)
    {
        return query.Select(e => new ActivityBacklogProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                IsArchived = e.Activity.IsArchived,
                Role = new ActivityRoleResponse
                {
                    Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text,
                    Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon
                },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text,
                        Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon
                    }
            },
            LocationTypeId = e.LocationTypeId,
            LocationType = new SelectOptionResponse { Id = e.LocationType.Id, Text = e.LocationType.Text },
            WeatherDependencyId = e.WeatherDependencyId,
            WeatherDependency = new SelectOptionResponse { Id = e.WeatherDependency.Id, Text = e.WeatherDependency.Text },
            EnergyLevel = e.EnergyLevel,
            EffortType = e.EffortType,
            MinParticipants = e.MinParticipants,
            MaxParticipants = e.MaxParticipants,
            ExpectedCostTierId = e.ExpectedCostTierId,
            ExpectedCostTier = new SelectOptionResponse { Id = e.ExpectedCostTier.Id, Text = e.ExpectedCostTier.Text },
            DurationMinutes = e.DurationMinutes,
            IsRepeatable = e.IsRepeatable,
            WeatherDependencyCode = e.WeatherDependency.Code
        });
    }

    /// <summary>
    /// Fills <see cref="WeatherDependencyCode"/> from the label when the projection found no stored code. Run
    /// on every row after it is loaded — see the two-stage note on the property.
    /// </summary>
    public ActivityBacklogProfileResponse WithEffectiveWeatherDependencyCode() =>
        this with { WeatherDependencyCode = WeatherDependencyCodes.Effective(WeatherDependencyCode, WeatherDependency.Text) };

    /// <summary>
    /// <see cref="Projection"/> plus the two completion fields, computed as correlated subqueries over
    /// <paramref name="anchors"/> so both stay sortable and filterable in SQL. Pass an anchor set that is
    /// already scoped to the caller.
    /// </summary>
    public static IQueryable<ActivityBacklogProfileResponse> ProjectionWithAnchors(
        IQueryable<ActivityBacklogProfile> query,
        IQueryable<MemoryAnchor> anchors)
    {
        return query.Select(e => new ActivityBacklogProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                IsArchived = e.Activity.IsArchived,
                Role = new ActivityRoleResponse
                {
                    Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text,
                    Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon
                },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text,
                        Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon
                    }
            },
            LocationTypeId = e.LocationTypeId,
            LocationType = new SelectOptionResponse { Id = e.LocationType.Id, Text = e.LocationType.Text },
            WeatherDependencyId = e.WeatherDependencyId,
            WeatherDependency = new SelectOptionResponse { Id = e.WeatherDependency.Id, Text = e.WeatherDependency.Text },
            EnergyLevel = e.EnergyLevel,
            EffortType = e.EffortType,
            MinParticipants = e.MinParticipants,
            MaxParticipants = e.MaxParticipants,
            ExpectedCostTierId = e.ExpectedCostTierId,
            ExpectedCostTier = new SelectOptionResponse { Id = e.ExpectedCostTier.Id, Text = e.ExpectedCostTier.Text },
            DurationMinutes = e.DurationMinutes,
            IsRepeatable = e.IsRepeatable,
            WeatherDependencyCode = e.WeatherDependency.Code,
            IsAnchored = !e.IsRepeatable && anchors.Any(m => m.ActivityId == e.ActivityId),
            MemoryAnchorId = e.IsRepeatable
                ? null
                : anchors
                    .Where(m => m.ActivityId == e.ActivityId)
                    .OrderByDescending(m => m.AnchorYear)
                    .ThenByDescending(m => m.AnchorMonth)
                    .ThenByDescending(m => m.Id)
                    .Select(m => (long?)m.Id)
                    .FirstOrDefault()
        });
    }
}
