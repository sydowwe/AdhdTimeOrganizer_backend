using AdhdTimeOrganizer.application.dto.response.suggestion;
using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.suggestion;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSuggestionsTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<List<TemplateSuggestionResponse>>
{
    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public override void Configure()
    {
        Get("/task-planner-day-template/suggestions/{calendarId:long:required}");
        
        
        Summary(s =>
        {
            s.Summary = "Get template suggestions for a date";
            s.Description = "Returns template suggestions based on day-of-week and day-type patterns";
            s.Response<List<TemplateSuggestionResponse>>(200, "Success");
            s.Response(400, "Invalid date format");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var calendarId = Route<long>("calendarId");
        var calendar = await dbContext.Set<Calendar>().FindAsync([calendarId], ct);

        if (calendar == null)
        {
            AddError("Calendar not found");
            await Send.ErrorsAsync(404, ct);
            return;
        }
        var date = calendar.Date;
        var userId = User.GetId();
        var isoDayOfWeek = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

        var patterns = await dbContext.Set<TemplateSuggestionPattern>()
            .Where(p => p.UserId == userId &&
                ((p.PatternType == 0 && p.PatternValue == isoDayOfWeek) ||
                 (p.PatternType == 1 && p.PatternValue == (int)calendar.DayType)))
            .Include(p => p.Template)
            .ToListAsync(ct);

        var result = patterns
            .OrderByDescending(p => p.OccurrenceCount)
            .Select(p => new TemplateSuggestionResponse
            {
                Template = TaskPlannerDayTemplateResponse.FromEntity(p.Template),
                PatternType = p.PatternType,
                PatternLabel = p.PatternType == 0
                    ? DayNames[p.PatternValue - 1]
                    : ((DayType)p.PatternValue).ToString(),
                OccurrenceCount = p.OccurrenceCount
            })
            .ToList();

        await Send.OkAsync(result, ct);
    }
}
