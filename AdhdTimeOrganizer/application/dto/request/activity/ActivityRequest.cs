using AdhdTimeOrganizer.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivityRequest : NameTextRequest, IMyRequest<Activity>
{
    public required bool IsUnavoidable { get; init; }


    public required long RoleId { get; init; }

    public long? CategoryId { get; init; }

    public Activity ToEntity => new() { Name = Name, Text = Text, IsUnavoidable = IsUnavoidable, RoleId = RoleId, CategoryId = CategoryId };

    public void UpdateEntity(Activity e)
    {
        e.Name = Name;
        e.Text = Text;
        e.IsUnavoidable = IsUnavoidable;
        e.RoleId = RoleId;
        e.CategoryId = CategoryId;
    }
}