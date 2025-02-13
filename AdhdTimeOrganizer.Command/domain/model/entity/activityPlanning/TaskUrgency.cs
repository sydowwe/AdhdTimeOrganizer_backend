using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

public class TaskUrgency : BaseTextColorEntity
{
    public int Priority { get; set; }
    public ICollection<ToDoList> ToDoListColl { get; set; } = new List<ToDoList>();
}