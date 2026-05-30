using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity;


namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityResponse : NameTextResponse, IProjectionResponse<ActivityResponse, Activity>
{
    public bool IsOnTodoList { get; init; }
    public bool IsUnavoidable { get; init; }
    public required ActivityRoleResponse Role { get; init; }
    public ActivityCategoryResponse? Category { get; init; }

    public static IQueryable<ActivityResponse> Projection(IQueryable<Activity> query) =>
        query.Select(e => new ActivityResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            IsUnavoidable = e.IsUnavoidable,
            Role = new ActivityRoleResponse { Id = e.Role.Id, Name = e.Role.Name, Text = e.Role.Text, Color = e.Role.Color, Icon = e.Role.Icon },
            Category = e.Category == null ? null : new ActivityCategoryResponse { Id = e.Category.Id, Name = e.Category.Name, Text = e.Category.Text, Color = e.Category.Color, Icon = e.Category.Icon },
        });

    public static ActivityResponse FromEntity(Activity e) =>
        new()
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            IsUnavoidable = e.IsUnavoidable,
            Role = ActivityRoleResponse.FromEntity(e.Role),
            Category = e.Category == null ? null : ActivityCategoryResponse.FromEntity(e.Category),
        };
}
