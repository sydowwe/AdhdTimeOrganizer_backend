using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activity;

public class Activity : BaseNameTextEntity
{
    public bool IsOnToDoList { get; set; }
    public bool IsUnavoidable { get; set; }

    public long RoleId { get; set; }
    public virtual Role Role { get; set; }
    public long? CategoryId { get; set; }
    public virtual Category Category { get; set; }

    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();

    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();
    public virtual ICollection<RoutineToDoList> RoutineToDoListColl { get; set; } = new List<RoutineToDoList>();
    public virtual ICollection<ToDoList> ToDoListColl { get; set; } = new List<ToDoList>();

}