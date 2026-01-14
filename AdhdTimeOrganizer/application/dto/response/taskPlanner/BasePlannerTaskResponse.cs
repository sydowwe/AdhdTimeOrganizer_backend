using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record BasePlannerTaskResponse : IdResponse
{
    public required TimeDto StartTime { get; set; }
    public required TimeDto EndTime { get; set; }
    public required bool IsBackground { get; set; }
    public required bool IsOptional { get; set; }

    public string? Location { get; set; }
    public string? Notes { get; set; }

    public required ActivityResponse Activity { get; set; }
    public TaskImportanceResponse? Importance { get; init; }
}