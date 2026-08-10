using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

public record ActivityRoleRequest : NameTextColorIconRequest, IMyRequest<ActivityRole>
{
    ActivityRole ICreateRequest<ActivityRole>.ToEntity => new() { Name = Name, Text = Text, Color = Color, Icon = Icon };

    void IUpdateRequest<ActivityRole>.UpdateEntity(ActivityRole e)
    {
        e.Name = Name;
        e.Text = Text;
        e.Color = Color;
        e.Icon = Icon;
    }
}