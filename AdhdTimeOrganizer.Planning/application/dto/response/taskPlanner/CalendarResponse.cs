using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

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

    /// <summary>
    /// The user's day-plan completion streak. A user-level fact, not a property of this day — it is carried
    /// here only because the home page fetches this response on every mount and a dedicated route would be one
    /// more request for a number that is needed every time.
    /// <para>
    /// <b>Null everywhere except <c>GetByDateCalendarEndpoint</c></b>, which fills it in after the projection.
    /// <see cref="Projection"/> cannot produce it: a streak is a walk across days, and this projection runs
    /// per-row. The grid and by-id reads leave it null rather than paying for a second query nothing displays.
    /// </para>
    /// </summary>
    public PlannerStreakResponse? Streak { get; init; }

    public static IQueryable<CalendarResponse> Projection(IQueryable<Calendar> query)
    {
        return query.Select(c => new CalendarResponse
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
            CompletedTasks = c.Tasks.Count(t => t.Status == PlannerTaskStatus.Completed)
        });
    }
}