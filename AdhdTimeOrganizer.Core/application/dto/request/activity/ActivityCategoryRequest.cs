using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

public record ActivityCategoryRequest : NameTextColorIconRequest, IMyRequest<ActivityCategory>
{
    ActivityCategory ICreateRequest<ActivityCategory>.ToEntity => new() { Name = Name, Text = Text, Color = Color, Icon = Icon };

    void IUpdateRequest<ActivityCategory>.UpdateEntity(ActivityCategory e)
    {
        e.Name = Name;
        e.Text = Text;
        e.Color = Color;
        e.Icon = Icon;
    }
}