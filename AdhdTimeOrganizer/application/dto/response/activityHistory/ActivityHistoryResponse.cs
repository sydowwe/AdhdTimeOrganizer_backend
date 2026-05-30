using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.extendable;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;


namespace AdhdTimeOrganizer.application.dto.response.activityHistory;

public record ActivityHistoryResponse : WithActivityResponse, IProjectionResponse<ActivityHistoryResponse, ActivityHistory>
{
    public required DateTime StartTimestamp { get; init; }
    public required IntTime Length { get; init; }
    public required DateTime EndTimestamp { get; init; }

    public static IQueryable<ActivityHistoryResponse> Projection(IQueryable<ActivityHistory> query) =>
        query.Select(e => new ActivityHistoryResponse
        {
            Id = e.Id,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                Role = new ActivityRoleResponse { Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text, Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon },
                Category = e.Activity.Category == null ? null : new ActivityCategoryResponse { Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text, Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon },
            },
            StartTimestamp = e.StartTimestamp,
            Length = e.Length,
            EndTimestamp = e.EndTimestamp,
        });

    public static ActivityHistoryResponse FromEntity(ActivityHistory e) =>
        new()
        {
            Id = e.Id,
            Activity = ActivityResponse.FromEntity(e.Activity),
            StartTimestamp = e.StartTimestamp,
            Length = e.Length,
            EndTimestamp = e.EndTimestamp,
        };
}
