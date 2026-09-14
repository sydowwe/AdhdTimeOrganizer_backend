using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

public record ActivityHistoryResponse : WithActivityResponse, IProjectionResponse<ActivityHistoryResponse, ActivityHistory>
{
    public required DateTime StartTimestamp { get; init; }
    public required IntTime Length { get; init; }
    public required DateTime EndTimestamp { get; init; }

    public static IQueryable<ActivityHistoryResponse> Projection(IQueryable<ActivityHistory> query)
    {
        return query.Select(e => new ActivityHistoryResponse
        {
            Id = e.Id,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                // Left unprojected, this defaulted to false on every row. That was invisible while archived
                // activities' sessions were being dropped from the list altogether; now that they are
                // returned (see ActivityHistoryReadExtensions) it would be a lie the client cannot detect —
                // a retired activity rendered exactly like a live one, in the one list that shows both.
                IsArchived = e.Activity.IsArchived,
                Role = new ActivityRoleResponse { Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text, Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                        { Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text, Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon }
            },
            StartTimestamp = e.StartTimestamp,
            Length = e.Length,
            EndTimestamp = e.EndTimestamp
        });
    }
}