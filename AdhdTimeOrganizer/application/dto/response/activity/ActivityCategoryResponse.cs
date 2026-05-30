using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity;


namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityCategoryResponse : NameTextColorIconResponse, IProjectionResponse<ActivityCategoryResponse, ActivityCategory>
{
    public string? Role { get; init; }

    public static IQueryable<ActivityCategoryResponse> Projection(IQueryable<ActivityCategory> query) =>
        query.Select(e => new ActivityCategoryResponse { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon });

    public static ActivityCategoryResponse FromEntity(ActivityCategory e) =>
        new() { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon, Role = e.Activities.FirstOrDefault()?.Role?.Name };
}
