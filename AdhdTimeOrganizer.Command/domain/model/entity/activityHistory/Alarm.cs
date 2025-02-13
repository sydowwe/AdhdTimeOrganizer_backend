using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

public class Alarm : BaseEntityWithActivity
{
    public DateTime StartTimestamp { get; set; }
    public bool IsActive { get; set; } = true;
}