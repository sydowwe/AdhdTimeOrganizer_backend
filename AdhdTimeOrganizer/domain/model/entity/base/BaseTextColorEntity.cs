using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.@base;

public class BaseTextColorEntity : BaseEntityWithUser
{
    public required string Text { get; set; }
    public string Color { get; set; } = "1A237E";
}