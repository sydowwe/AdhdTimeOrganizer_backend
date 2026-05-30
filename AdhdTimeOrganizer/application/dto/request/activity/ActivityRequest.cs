using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivityRequest : NameTextRequest, IMyRequest<Activity>
{
    [Required]
    public required bool IsUnavoidable { get; init; }

    [Required]
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
