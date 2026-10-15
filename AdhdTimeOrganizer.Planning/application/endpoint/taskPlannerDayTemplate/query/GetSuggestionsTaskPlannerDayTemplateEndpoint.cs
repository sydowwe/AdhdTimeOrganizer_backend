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

    // TemplateSuggestionResponse.PatternType. 0 and 1 are the view's own values; 2, 3 and 5 carry no history; 4 is the
    // stretch of time away the day belongs to (F4-02); 6 is a weekend match on a public holiday that falls on a weekday
    // (F4 Q4), from history or from the template's settings.
    private const int HistoryDayOfWeek = 0;
    private const int HistoryDayType = 1;
    private const int MetadataScheduledDay = 2;
    private const int MetadataDayType = 3;
    private const int PlannedForTimeAway = 4;
    private const int MetadataLocation = 5;
    private const int HolidayAsWeekend = 6;

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/task-planner-day-template/suggestions/{calendarId:long:required}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = "Get template suggestions for a date";
            s.Description = "Returns active templates suggested for the calendar day, one entry per template. On a day " +
                            "inside a stretch of time away that names a template, that template comes first " +
                            "(pattern type 4, label = the stretch's name). Then those applied on this weekday or day " +
                            "type before (by occurrence count), then those whose SuggestedLocation, ScheduledDays or " +
                            "SuggestedForDayType match the day, a location match first (pattern type 5, label = the " +
                            "Location member name). On an away day (Vacation, SickDay) every day-type or location match " +
                            "ranks above every weekday match. On a public holiday that falls on a weekday, templates " +
                            "applied on or suggested for weekends come first, with any location match (pattern type 6, " +
                            "label = the holiday key), then the day ranks as the workday it is stored as";
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

        // Every type the day is matched by, strongest first: the stored one, or for a public holiday on a weekday,
        // Weekend and then Workday (F4 Q4). The view records past days by their stored type, so a holiday Monday is
        // read against both the weekend and the workday statistics.
        var dayTypes = calendar.DayType.PlanningDayTypes(calendar.HolidayKey);
        var dayTypeValues = dayTypes.Select(t => (int)t).ToArray();

        var patterns = await dbContext.Set<PlannerSuggestionFromDayTemplate>()
            .Where(p => p.UserId == userId &&
                        ((p.PatternType == HistoryDayOfWeek && p.PatternValue == isoDayOfWeek) ||
                         (p.PatternType == HistoryDayType && dayTypeValues.Contains(p.PatternValue))))
            .ToListAsync(ct);

        var result = new List<TemplateSuggestionResponse>();
        var coveredTemplateIds = new HashSet<long>();

        // 0. The template the user chose for the stretch of time away this day belongs to (F4-02): an explicit choice
        // for exactly these days, so it outranks any history. Only suggested — the stretch never applies it. Held to
        // the same active-template set as every other tier, so a template the user switched off is not offered here
        // either. Before the away/not-away split: a sick day inside a vacation still belongs to the trip.
        if (calendar.TimeAwayId is { } timeAwayId)
        {
            var stretch = await dbContext.Set<TimeAway>()
                .Where(t => t.Id == timeAwayId)
                .Select(t => new { t.Name, t.TemplateId })
                .FirstOrDefaultAsync(ct);

            if (stretch?.TemplateId is { } stretchTemplateId
                && templates.TryGetValue(stretchTemplateId, out var stretchTemplate)
                && coveredTemplateIds.Add(stretchTemplateId))
            {
                result.Add(new TemplateSuggestionResponse
                {
                    Template = stretchTemplate,
                    PatternType = PlannedForTimeAway,
                    // The user's own words, not an invariant member name: "Planned for Barcelona".
                    PatternLabel = stretch.Name,
                    OccurrenceCount = 0
                });
            }
        }

        if (!calendar.DayType.IsAway())
        {
            // 0b. A public holiday on a weekday ranks as the weekend it is to most people (F4 Q4). Weekend history and
            // weekend templates come first, with the location matches (which, as on an away day, are about this one
            // day), and are reported as the holiday rather than as "Weekend", since the user sees a workday that is
            // a holiday. Everything else is demoted, not dropped: the day is still a workday for whoever works it, so
            // the tiers below follow exactly as they would on any other workday. Empty on every other day.
            foreach (var rankedAs in dayTypes.Where(t => t != calendar.DayType))
            {
                AddHistory(patterns.Where(p => p.PatternType == HistoryDayType && p.PatternValue == (int)rankedAs)
                    .OrderByDescending(p => p.OccurrenceCount), throughHoliday: true);
                AddMetadata(rankedAs, m => m.MatchesDayType || m.MatchesLocation, reportDayType: true, throughHoliday: true);
            }

            // 1. History: what the user actually applied on this weekday / day type, most-applied first.
            AddHistory(patterns.OrderByDescending(p => p.OccurrenceCount).ThenBy(p => p.PatternType));

            // 2. What the user set on the template, for templates history has not already suggested. Weaker
            // evidence than history, so it ranks after it. A location match ranks first and is reported as such (C3):
            // the day's location is set for this one day, by the user or by the stretch it belongs to, so it says more
            // than a recurring weekday — on a Wednesday marked Office, "Office day" beats a "Home office" scheduled
            // for Wednesdays. Then a template matching both its weekday and its day type, reporting the weekday
            // match, the more specific of the two.
            AddMetadata(calendar.DayType, m => m.MatchesDay || m.MatchesDayType || m.MatchesLocation, reportDayType: false);
        }
        else
        {
            // An away day (vacation, sick): what kind of day it is outranks which weekday it is. Twenty Mondays of
            // "Office" must not bury the "Beach" template on a Monday the user marked as vacation. Weekday matches
            // are demoted, not dropped — some users keep one template for everything. The location is, like the
            // type, about this day, so it ranks with the day-type matches and ahead of them.
            AddHistory(patterns.Where(p => p.PatternType == HistoryDayType).OrderByDescending(p => p.OccurrenceCount));
            AddMetadata(calendar.DayType, m => m.MatchesDayType || m.MatchesLocation, reportDayType: true);
            AddHistory(patterns.Where(p => p.PatternType == HistoryDayOfWeek).OrderByDescending(p => p.OccurrenceCount));
            AddMetadata(calendar.DayType, m => m.MatchesDay, reportDayType: false);
        }

        await Send.OkAsync(result, ct);
        return;

        // One entry per template, first tier wins — a template applied on Thursdays that were also Workdays is in
        // the view once per pattern type. Through a holiday, a weekend pattern is reported as the holiday (type 6,
        // the stable key the client translates) and keeps its count: it is still history.
        void AddHistory(IEnumerable<PlannerSuggestionFromDayTemplate> ordered, bool throughHoliday = false)
        {
            foreach (var p in ordered)
            {
                if (!templates.TryGetValue(p.TemplateId, out var template) || !coveredTemplateIds.Add(p.TemplateId))
                    continue;

                result.Add(new TemplateSuggestionResponse
                {
                    Template = template,
                    PatternType = throughHoliday ? HolidayAsWeekend : p.PatternType,
                    PatternLabel = throughHoliday
                        ? calendar.HolidayKey!
                        : p.PatternType == HistoryDayOfWeek
                            ? DayNames[p.PatternValue - 1]
                            : ((DayType)p.PatternValue).ToString(),
                    OccurrenceCount = p.OccurrenceCount
                });
            }
        }

        // dayType is the type MatchesDayType compares the template's SuggestedForDayType with: the stored one, or the
        // weekend a holiday weekday ranks as.
        void AddMetadata(
            DayType dayType,
            Func<(TaskPlannerDayTemplateResponse Template, bool MatchesDay, bool MatchesDayType, bool MatchesLocation), bool> include,
            bool reportDayType,
            bool throughHoliday = false)
        {
            var matches = templates.Values
                .Where(t => !coveredTemplateIds.Contains(t.Id))
                .Select(t => (Template: t,
                    MatchesDay: t.ScheduledDays.Contains(date.DayOfWeek),
                    MatchesDayType: t.SuggestedForDayType == dayType,
                    // A null on either side never matches: most days carry no location, and most templates none.
                    MatchesLocation: calendar.Location is { } dayLocation && t.SuggestedLocation == dayLocation))
                .Where(include)
                .OrderByDescending(m => m.MatchesLocation)
                .ThenByDescending(m => m.MatchesDay && m.MatchesDayType)
                .ThenByDescending(m => m.Template.UsageCount)
                .ThenBy(m => m.Template.Id)
                .ToList();

            foreach (var m in matches)
            {
                coveredTemplateIds.Add(m.Template.Id);
                // Invariant enum member names and the holiday key — the client translates them, so they must never be
                // localized here.
                var (patternType, patternLabel) = m.MatchesLocation
                    ? (MetadataLocation, calendar.Location!.Value.ToString())
                    : throughHoliday
                        ? (HolidayAsWeekend, calendar.HolidayKey!)
                        : (reportDayType ? m.MatchesDayType : !m.MatchesDay)
                            ? (MetadataDayType, dayType.ToString())
                            : (MetadataScheduledDay, date.DayOfWeek.ToString());
                result.Add(new TemplateSuggestionResponse
                {
                    Template = m.Template,
                    PatternType = patternType,
                    PatternLabel = patternLabel,
                    OccurrenceCount = 0
                });
            }
        }
    }
}