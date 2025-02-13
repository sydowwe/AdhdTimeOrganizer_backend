using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

public class RoutineToDoList : BaseEntityWithIsDone
{
    public virtual RoutineTimePeriod TimePeriod { get; set; }
    public required long TimePeriodId { get; set; }
}