using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

public class ToDoList : BaseEntityWithIsDone
{
    public virtual TaskUrgency TaskUrgency { get; set; }
    public long TaskUrgencyId { get; set; }
}