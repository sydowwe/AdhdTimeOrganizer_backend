using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

public class PlannerTask : BaseEntityWithIsDone
{
    public required DateTime StartTimestamp { get; set; }
    public required int MinuteLength { get; set; }
    public required string Color { get; set; } = "#1A237E";
}