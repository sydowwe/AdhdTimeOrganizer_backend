using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record CalendarResponse : IdResponse
{
    public required DateOnly Date { get; set; }

    public DayType DayType { get; set; }
    public string? Label { get; set; }

    public TimeOnly? WakeUpTime { get; set; }
    public TimeOnly? BedTime { get; set; }

    public bool IsPlanned { get; set; }
    public long? AppliedTemplateId { get; set; }
    public string? AppliedTemplateName { get; set; }

    public string? Weather { get; set; }
    public string? Notes { get; set; }

    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }

    public int CompletionRate => TotalTasks > 0 ? (CompletedTasks * 100) / TotalTasks : 0;
}