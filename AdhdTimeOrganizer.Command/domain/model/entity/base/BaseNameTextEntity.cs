using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.domain.model.entity.user;

namespace AdhdTimeOrganizer.Command.domain.model.entity.@base;

public abstract class BaseNameTextEntity : BaseEntityWithUser
{
    public string Name { get; set; }

    public string? Text { get; set; }
}