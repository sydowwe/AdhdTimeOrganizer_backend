using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.application.dto.request.@base;

public record NameTextColorIconRequest : NameTextColorRequest,
    ICreateRequest<ActivityCategory>, IUpdateRequest<ActivityCategory>,
    ICreateRequest<ActivityRole>, IUpdateRequest<ActivityRole>
{
    [StringLength(255)]
    public string? Icon { get; init; }

    ActivityCategory ICreateRequest<ActivityCategory>.ToEntity => new() { UserId = 0, Name = Name, Text = Text, Color = Color, Icon = Icon };
    void IUpdateRequest<ActivityCategory>.UpdateEntity(ActivityCategory e) { e.Name = Name; e.Text = Text; e.Color = Color; e.Icon = Icon; }

    ActivityRole ICreateRequest<ActivityRole>.ToEntity => new() { UserId = 0, Name = Name, Text = Text, Color = Color, Icon = Icon };
    void IUpdateRequest<ActivityRole>.UpdateEntity(ActivityRole e) { e.Name = Name; e.Text = Text; e.Color = Color; e.Icon = Icon; }
}
