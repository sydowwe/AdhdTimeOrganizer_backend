using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.domain.model.entity.@base;

public abstract class BaseNameTextColorEntity : BaseNameTextEntity
{
    public string Color { get; set; } = "1A237E";
}