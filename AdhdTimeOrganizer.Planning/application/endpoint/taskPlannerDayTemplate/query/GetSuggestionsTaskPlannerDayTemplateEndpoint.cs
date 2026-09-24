using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSuggestionsTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<List<TemplateSuggestionResponse>>
{
    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    // TemplateSuggestionResponse.PatternType. 0 and 1 are the view's own values; 2 and 3 carry no history.
    private const int HistoryDayOfWeek = 0;
    private const int HistoryDayType = 1;
    private const int MetadataScheduledDay = 2;
    private const int MetadataDayType = 3;

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/task-planner-day-template/suggestions/{calendarId:long:required}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = "Get template suggestions for a date";
            s.Description = "Returns active templates suggested for the calendar day, one entry per template: first those " +
                            "applied on this weekday or day type before (by occurrence count), then those whose " +
                            "ScheduledDays or SuggestedForDayType match the day";
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

        // Every candidate — history-backed or not — must be one of these. A user has a handful of templates, so
        // they are loaded whole: ScheduledDays is a value-converted collection with no translatable Contains.
        // Loading them here also covers what the view cannot: it is not filtered by IsActive, and its rows
        // outlive a deleted template until the host-side refresh job runs again (CQ-13) — a pattern whose
        // template is not in this set is skipped either way.
        var templates = await TaskPlannerDayTemplateResponse.Projection(
                dbContext.Set<TaskPlannerDayTemplate>().Where(t => t.UserId == userId && t.IsActive))
            .ToDictionaryAsync(t => t.Id, ct);

        var patterns = await dbContext.Set<PlannerSuggestionFromDayTemplate>()
            .Where(p => p.UserId == userId &&
                        ((p.PatternType == HistoryDayOfWeek && p.PatternValue == isoDayOfWeek) ||
                         (p.PatternType == HistoryDayType && p.PatternValue == (int)calendar.DayType)))
            .ToListAsync(ct);

        var result = new List<TemplateSuggestionResponse>();
        var coveredTemplateIds = new HashSet<long>();

        // 1. History: what the user actually applied on this weekday / day type. One entry per template — a
        // template applied twice on Thursdays that were also Workdays is in the view once per pattern type.
        foreach (var p in patterns
                     .OrderByDescending(p => p.OccurrenceCount)
                     .ThenBy(p => p.PatternType))
        {
            if (!templates.TryGetValue(p.TemplateId, out var template) || !coveredTemplateIds.Add(p.TemplateId))
                continue;

            result.Add(new TemplateSuggestionResponse
            {
                Template = template,
                PatternType = p.PatternType,
                PatternLabel = p.PatternType == HistoryDayOfWeek
                    ? DayNames[p.PatternValue - 1]
                    : ((DayType)p.PatternValue).ToString(),
                OccurrenceCount = p.OccurrenceCount
            });
        }

        // 2. What the user set on the template, for templates history has not already suggested. Weaker evidence
        // than history, so it ranks after it; a template matching both its weekday and its day type ranks first
        // and reports the weekday match, the more specific of the two.
        var metadataMatches = templates.Values
            .Where(t => !coveredTemplateIds.Contains(t.Id))
            .Select(t => (Template: t,
                MatchesDay: t.ScheduledDays.Contains(date.DayOfWeek),
                MatchesDayType: t.SuggestedForDayType == calendar.DayType))
            .Where(m => m.MatchesDay || m.MatchesDayType)
            .OrderByDescending(m => m.MatchesDay && m.MatchesDayType)
            .ThenByDescending(m => m.Template.UsageCount)
            .ThenBy(m => m.Template.Id);

        foreach (var m in metadataMatches)
        {
            result.Add(new TemplateSuggestionResponse
            {
                Template = m.Template,
                PatternType = m.MatchesDay ? MetadataScheduledDay : MetadataDayType,
                // Invariant enum member names — the client translates them, so they must never be localized here.
                PatternLabel = m.MatchesDay ? date.DayOfWeek.ToString() : calendar.DayType.ToString(),
                OccurrenceCount = 0
            });
        }

        await Send.OkAsync(result, ct);
    }
}