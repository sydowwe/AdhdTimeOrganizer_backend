using AdhdTimeOrganizer.Command.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activity;

public abstract class BaseEntityWithActivity : BaseEntityWithUser
{
    public long ActivityId { get; set; }
    public virtual Activity Activity { get; set; }
}