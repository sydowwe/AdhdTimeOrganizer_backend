using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <inheritdoc cref="ActivityBacklogProfileResponse"/>
public record MemoryAnchorResponse : WithActivityResponse, IProjectionResponse<MemoryAnchorResponse, MemoryAnchor>
{
    public required long ActivityId { get; init; }
    public required int AnchorMonth { get; init; }
    public required int AnchorYear { get; init; }
    public required string HighlightNote { get; init; }
    public required int Rating { get; init; }

    /// <summary>
    /// The anchor's activity also has a bucket-list profile — i.e. this anchor is what marked that entry
    /// done. Backs the grid's <em>Source</em> column, which says where the anchored experience came from.
    /// </summary>
    /// <remarks>
    /// Not mutually exclusive with <see cref="HasBacklog"/>: one activity may carry both profiles, and the
    /// column shows both. Derived on read for the same reason
    /// <see cref="ActivityBucketListProfileResponse.IsAnchored"/> is — the profile rows are the record, and
    /// a copy of that fact on the anchor is one more thing to keep in step.
    /// </remarks>
    public bool HasBucketList { get; init; }

    /// <inheritdoc cref="HasBucketList"/>
    public bool HasBacklog { get; init; }

    /// <summary>
    /// Whether that backlog entry is repeatable, which is what decides whether anchoring closed it — only a
    /// non-repeatable entry is closed by being anchored. <c>null</c> when <see cref="HasBacklog"/> is false:
    /// "is the backlog entry repeatable" has no answer when there is no backlog entry, and a <c>false</c>
    /// there would read as "it is one-time".
    /// </summary>
    /// <remarks>
    /// Named for the column it reports, in the one direction the solution uses — see
    /// <see cref="ActivityBacklogProfileResponse.IsRepeatable"/>.
    /// </remarks>
    public bool? BacklogIsRepeatable { get; init; }

    /// <summary>
    /// Leaves the three Source fields at their defaults: the profiles are not reachable by navigation from
    /// the anchor, and this signature has no DbContext to reach them with. Callers that need them use
    /// <see cref="ProjectionWithProfiles"/>.
    /// </summary>
    public static IQueryable<MemoryAnchorResponse> Projection(IQueryable<MemoryAnchor> query)
    {
        return query.Select(e => new MemoryAnchorResponse
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
            AnchorMonth = e.AnchorMonth,
            AnchorYear = e.AnchorYear,
            HighlightNote = e.HighlightNote,
            Rating = e.Rating
        });
    }

    /// <summary>
    /// <see cref="Projection"/> plus the three Source fields, computed as correlated subqueries over the
    /// two profile sets so all three stay sortable in SQL. Pass sets already scoped to the caller.
    /// </summary>
    public static IQueryable<MemoryAnchorResponse> ProjectionWithProfiles(
        IQueryable<MemoryAnchor> query,
        IQueryable<ActivityBucketListProfile> bucketLists,
        IQueryable<ActivityBacklogProfile> backlogs)
    {
        return query.Select(e => new MemoryAnchorResponse
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
            AnchorMonth = e.AnchorMonth,
            AnchorYear = e.AnchorYear,
            HighlightNote = e.HighlightNote,
            Rating = e.Rating,
            HasBucketList = bucketLists.Any(p => p.ActivityId == e.ActivityId),
            HasBacklog = backlogs.Any(p => p.ActivityId == e.ActivityId),
            BacklogIsRepeatable = backlogs
                .Where(p => p.ActivityId == e.ActivityId)
                .Select(p => (bool?)p.IsRepeatable)
                .FirstOrDefault()
        });
    }
}
