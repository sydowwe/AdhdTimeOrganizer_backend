using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity;


namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityRoleResponse : NameTextColorIconResponse, IProjectionResponse<ActivityRoleResponse, ActivityRole>
{
    public static IQueryable<ActivityRoleResponse> Projection(IQueryable<ActivityRole> query) =>
        query.Select(e => new ActivityRoleResponse { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon });

    public static ActivityRoleResponse FromEntity(ActivityRole e) =>
        new() { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon };
}
