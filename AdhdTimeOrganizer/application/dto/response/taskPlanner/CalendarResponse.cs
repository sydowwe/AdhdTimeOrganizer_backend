using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record CalendarResponse : IdResponse, IProjectionResponse<CalendarResponse, Calendar>
{
    public required DateOnly Date { get; init; }

    public required int DayIndex { get; init; }
    public required DayType DayType { get; init; }
    public string? HolidayName { get; init; }
    public string? Label { get; init; }

    public required TimeDto WakeUpTime { get; init; }
    public required TimeDto BedTime { get; init; }

    public long? AppliedTemplateId { get; init; }
    public string? AppliedTemplateName { get; init; }

    public Location? Location { get; init; }

    public string? Weather { get; init; }
    public string? Notes { get; init; }

    public required int TotalTasks { get; init; }
    public required int CompletedTasks { get; init; }

    public static IQueryable<CalendarResponse> Projection(IQueryable<Calendar> query) =>
        query.Select(c => new CalendarResponse
        {
            Id = c.Id,
            Date = c.Date,
            DayIndex = c.Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)c.Date.DayOfWeek,
            DayType = c.DayType,
            HolidayName = c.HolidayName,
            Label = c.Label,
            WakeUpTime = new TimeDto(c.WakeUpTime.Hour, c.WakeUpTime.Minute),
            BedTime = new TimeDto(c.BedTime.Hour, c.BedTime.Minute),
            AppliedTemplateId = c.AppliedTemplateId,
            AppliedTemplateName = c.AppliedTemplateName,
            Location = c.Location,
            Weather = c.Weather,
            Notes = c.Notes,
            TotalTasks = c.Tasks.Count(),
            CompletedTasks = c.Tasks.Count(t => t.Status == PlannerTaskStatus.Completed),
        });

    public static CalendarResponse FromEntity(Calendar entity) => new()
    {
        Id = entity.Id,
        Date = entity.Date,
        DayIndex = entity.Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)entity.Date.DayOfWeek,
        DayType = entity.DayType,
        HolidayName = entity.HolidayName,
        Label = entity.Label,
        WakeUpTime = new TimeDto(entity.WakeUpTime.Hour, entity.WakeUpTime.Minute),
        BedTime = new TimeDto(entity.BedTime.Hour, entity.BedTime.Minute),
        AppliedTemplateId = entity.AppliedTemplateId,
        AppliedTemplateName = entity.AppliedTemplateName,
        Location = entity.Location,
        Weather = entity.Weather,
        Notes = entity.Notes,
        TotalTasks = entity.Tasks.Count(),
        CompletedTasks = entity.Tasks.Count(t => t.Status == PlannerTaskStatus.Completed),
    };
}