using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using FastEndpoints;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetSuggestionsRepeatingPlannerTaskEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<List<SuggestionResponse>>
{
    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/repeating-planner-task/suggestions/{calendarId:long:required}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = "Get task suggestions for a date";
            s.Description = "Returns user-set and automatic (pattern-based) suggestions for the given date. On an away " +
                            "day (Vacation, SickDay) only the user-set ones";
            s.Response<List<SuggestionResponse>>(200, "Success");
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
        var dayOfMonth = date.Day;

        var result = new List<SuggestionResponse>();

        // An activity already on this day is not a suggestion: offering it again only invites a duplicate task.
        // Every status counts — a cancelled task is a decision about the day, not a gap in it. Seeding the
        // covered set with these suppresses all three tiers below.
        var coveredActivityIds = (await dbContext.Set<PlannerTask>()
                .Where(t => t.CalendarId == calendarId)
                .Select(t => t.ActivityId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        // 1. User-set suggestions (RepeatingPlannerTask)
        var tasks = await dbContext.Set<RepeatingPlannerTask>()
            .Where(t => t.UserId == userId && t.IsActive)
            // What SQL can decide, SQL decides. The active window is a plain column comparison, so it is
            // answered here rather than by loading every active recurrence and discarding most of them. The
            // three day matches use ScheduledDays / ScheduledDates / ScheduledForDayTypes, which are
            // value-converted collections with no translatable Contains, so those stay in memory below.
            // The window bounds every recurrence type, not only DateRange: a weekly or monthly rule may carry
            // one ("every Tue until 30 Nov"), and the planner shows it as the reason for the suggestion. An
            // unset bound is open. A DateRange rule needs both bounds, because the window is its whole rule.
            .Where(t => (t.ActiveFromDate == null || t.ActiveFromDate <= date)
                        && (t.ActiveToDate == null || date <= t.ActiveToDate))
            .Where(t => t.RecurrenceType != RecurrenceType.DateRange
                        || (t.ActiveFromDate != null && t.ActiveToDate != null))
            .Include(t => t.Importance)
            .Include(t => t.Activity).ThenInclude(a => a.Role)
            .Include(t => t.Activity).ThenInclude(a => a.Category)
            .ToListAsync(ct);

        var matchedTasks = tasks.Where(task => !coveredActivityIds.Contains(task.ActivityId) && task.RecurrenceType switch
        {
            RecurrenceType.DayOfWeek => task.ScheduledDays.Contains(date.DayOfWeek.ToString()),
            RecurrenceType.DayOfMonth => task.ScheduledDates.Contains(date.Day),
            RecurrenceType.DayType => task.ScheduledForDayTypes.Contains(calendar.DayType.ToString()),
            // Already decided by the WHERE above: a DateRange row that got this far is in range.
            RecurrenceType.DateRange => true,
            _ => false
        }).ToList();

        // One projection for the whole matched set. Projecting per match — Projection(new[] { task }
        // .AsQueryable()).Single() — built and compiled an expression tree per suggestion.
        var projected = RepeatingPlannerTaskResponse.Projection(matchedTasks.AsQueryable()).ToList();

        for (var i = 0; i < matchedTasks.Count; i++)
        {
            var task = matchedTasks[i];
            var r = projected[i];
            result.Add(new SuggestionResponse
            {
                RepeatingPlannerTaskId = task.Id,
                Activity = r.Activity,
                Importance = r.Importance,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                IsBackground = r.IsBackground,
                Location = r.Location,
                Notes = r.Notes,
                Color = r.Color,
                RecurrenceType = r.RecurrenceType,
                ScheduledDays = r.ScheduledDays,
                ScheduledDates = r.ScheduledDates,
                ActiveFromDate = r.ActiveFromDate,
                ActiveToDate = r.ActiveToDate,
                ScheduledForDayTypes = r.ScheduledForDayTypes,
                SourceType = SuggestionSourceType.UserSet,
                OccurrenceCount = null
            });
            coveredActivityIds.Add(task.ActivityId);
        }

        // On an away day (vacation, sick) only the recurrences the user set apply. Tiers 2 and 3 are statistics
        // over weekdays and days of the month, which workdays dominate — so a vacation Monday would be offered the
        // office Monday's standup and commute. A user who wants a recurrence off while away already has
        // RecurrenceType.DayType for it; a daily medication recurrence must still appear, and does, above.
        if (calendar.DayType.IsAway())
        {
            await Send.OkAsync(result, ct);
            return;
        }

        // 2. Planner-pattern suggestions
        var plannerPatterns = await dbContext.Set<PlannerSuggestionFromPlannerTask>()
            .Where(p => p.UserId == userId &&
                        ((p.PatternType == (int)RecurrenceType.DayOfWeek && p.PatternValue == isoDayOfWeek) ||
                         (p.PatternType == (int)RecurrenceType.DayOfMonth && p.PatternValue == dayOfMonth)))
            // A materialized-view row outlives its principal until the host-side refresh job runs again, and
            // deleting an Activity cascades its planner tasks away — so the aggregate can name an activity
            // that is gone. Dereferencing p.Activity.Role.Color would then 500 a page load.
            .Where(p => p.Activity != null)
            .Include(p => p.Activity).ThenInclude(a => a.Role)
            .Include(p => p.Activity).ThenInclude(a => a.Category)
            .Include(p => p.Importance)
            .ToListAsync(ct);

        var plannerCoveredKeys = new HashSet<(long, int, int)>();
        foreach (var p in plannerPatterns)
        {
            if (coveredActivityIds.Contains(p.ActivityId))
                continue;

            result.Add(MapPatternToSuggestion(p, SuggestionSourceType.PlannedPattern));
            plannerCoveredKeys.Add((p.ActivityId, p.PatternType, p.PatternValue));
        }

        // 3. Activity-history-pattern suggestions
        var historyPatterns = await dbContext.Set<PlannerSuggestionFromActivityHistory>()
            .Where(p => p.UserId == userId &&
                        ((p.PatternType == (int)RecurrenceType.DayOfWeek && p.PatternValue == isoDayOfWeek) ||
                         (p.PatternType == (int)RecurrenceType.DayOfMonth && p.PatternValue == dayOfMonth)))
            // Same staleness as the planner-pattern view above.
            .Where(p => p.Activity != null)
            .Include(p => p.Activity).ThenInclude(a => a.Role)
            .Include(p => p.Activity).ThenInclude(a => a.Category)
            .ToListAsync(ct);

        foreach (var p in historyPatterns)
        {
            if (coveredActivityIds.Contains(p.ActivityId))
                continue;
            if (plannerCoveredKeys.Contains((p.ActivityId, p.PatternType, p.PatternValue)))
                continue;

            result.Add(MapHistoryPatternToSuggestion(p));
        }

        await Send.OkAsync(result, ct);
    }

    private static SuggestionResponse MapPatternToSuggestion(PlannerSuggestionFromPlannerTask p, SuggestionSourceType sourceType)
    {
        var recurrenceType = (RecurrenceType)p.PatternType;
        return new SuggestionResponse
        {
            RepeatingPlannerTaskId = null,
            Activity = MapActivity(p.Activity),
            Importance = p.Importance != null ? MapImportance(p.Importance) : null,
            StartTime = new TimeDto(p.AvgStartTime.Hour, p.AvgStartTime.Minute),
            EndTime = new TimeDto(p.AvgEndTime.Hour, p.AvgEndTime.Minute),
            IsBackground = p.IsBackground,
            Location = null,
            Notes = null,
            Color = p.Activity.Role.Color,
            RecurrenceType = recurrenceType,
            ScheduledDays = recurrenceType == RecurrenceType.DayOfWeek ? [DayNames[p.PatternValue - 1]] : [],
            ScheduledDates = recurrenceType == RecurrenceType.DayOfMonth ? [p.PatternValue] : [],
            ActiveFromDate = null,
            ActiveToDate = null,
            ScheduledForDayTypes = [],
            SourceType = sourceType,
            OccurrenceCount = p.OccurrenceCount
        };
    }

    private static SuggestionResponse MapHistoryPatternToSuggestion(PlannerSuggestionFromActivityHistory p)
    {
        var recurrenceType = (RecurrenceType)p.PatternType;
        return new SuggestionResponse
        {
            RepeatingPlannerTaskId = null,
            Activity = MapActivity(p.Activity),
            Importance = null,
            StartTime = new TimeDto(p.AvgStartTime.Hour, p.AvgStartTime.Minute),
            EndTime = new TimeDto(p.AvgEndTime.Hour, p.AvgEndTime.Minute),
            IsBackground = false,
            Location = null,
            Notes = null,
            Color = p.Activity.Role.Color,
            RecurrenceType = recurrenceType,
            ScheduledDays = recurrenceType == RecurrenceType.DayOfWeek ? [DayNames[p.PatternValue - 1]] : [],
            ScheduledDates = recurrenceType == RecurrenceType.DayOfMonth ? [p.PatternValue] : [],
            ActiveFromDate = null,
            ActiveToDate = null,
            ScheduledForDayTypes = [],
            SourceType = SuggestionSourceType.HistoryPattern,
            OccurrenceCount = p.OccurrenceCount
        };
    }

    private static ActivityResponse MapActivity(Activity activity) =>
        new()
        {
            Id = activity.Id,
            Name = activity.Name,
            Text = activity.Text,
            IsUnavoidable = activity.IsUnavoidable,
            IsOnTodoList = false,
            Role = new ActivityRoleResponse
            {
                Id = activity.Role.Id,
                Name = activity.Role.Name,
                Text = activity.Role.Text,
                Color = activity.Role.Color,
                Icon = activity.Role.Icon
            },
            Category = activity.Category != null
                ? new ActivityCategoryResponse
                {
                    Id = activity.Category.Id,
                    Name = activity.Category.Name,
                    Text = activity.Category.Text,
                    Color = activity.Category.Color,
                    Icon = activity.Category.Icon
                }
                : null
        };

    private static TaskImportanceResponse MapImportance(TaskImportance importance) =>
        new()
        {
            Id = importance.Id,
            Text = importance.Text,
            Color = importance.Color,
            Icon = importance.Icon,
            Importance = importance.Importance
        };
}