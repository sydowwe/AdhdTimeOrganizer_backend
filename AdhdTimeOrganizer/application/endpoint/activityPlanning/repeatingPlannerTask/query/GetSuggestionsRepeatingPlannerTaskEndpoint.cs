using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.suggestion;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.suggestion;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetSuggestionsRepeatingPlannerTaskEndpoint(AppDbContext dbContext, RepeatingPlannerTaskMapper mapper)
    : EndpointWithoutRequest<List<SuggestionResponse>>
{
    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public override void Configure()
    {
        Get("/repeating-planner-task/suggestions");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Get task suggestions for a date";
            s.Description = "Returns user-set and automatic (pattern-based) suggestions for the given date";
            s.Response<List<SuggestionResponse>>(200, "Success");
            s.Response(400, "Invalid date format");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dateString = Query<string>("date");
        if (!DateOnly.TryParseExact(dateString, "yyyy-MM-dd", out var date))
        {
            AddError("Invalid date format. Use yyyy-MM-dd.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var userId = User.GetId();
        var isoDayOfWeek = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        var dayOfMonth = date.Day;

        var result = new List<SuggestionResponse>();
        var coveredActivityIds = new HashSet<long>();

        // 1. User-set suggestions (RepeatingPlannerTask)
        var tasks = await dbContext.Set<RepeatingPlannerTask>()
            .Where(t => t.UserId == userId && t.IsActive)
            .Include(t => t.Importance)
            .Include(t => t.Activity).ThenInclude(a => a.Role)
            .Include(t => t.Activity).ThenInclude(a => a.Category)
            .ToListAsync(ct);

        Calendar? calendar = null;
        if (tasks.Any(t => t.RecurrenceType == RecurrenceType.DayType))
        {
            calendar = await dbContext.Set<Calendar>()
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, ct);
        }

        foreach (var task in tasks)
        {
            var matches = task.RecurrenceType switch
            {
                RecurrenceType.DayOfWeek => task.ScheduledDays.Contains(date.DayOfWeek.ToString()),
                RecurrenceType.DayOfMonth => task.ScheduledDates.Contains(date.Day),
                RecurrenceType.DateRange => task.ActiveFromDate.HasValue && task.ActiveToDate.HasValue
                    && task.ActiveFromDate.Value <= date && date <= task.ActiveToDate.Value,
                RecurrenceType.DayType => calendar != null
                    && task.ScheduledForDayTypes.Contains(calendar.DayType.ToString()),
                _ => false
            };
            if (!matches) continue;

            var r = mapper.ToResponse(task);
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

        // 2. Planner-pattern suggestions
        var plannerPatterns = await dbContext.Set<PlannerTaskPattern>()
            .Where(p => p.UserId == userId &&
                ((p.PatternType == (int)RecurrenceType.DayOfWeek && p.PatternValue == isoDayOfWeek) ||
                 (p.PatternType == (int)RecurrenceType.DayOfMonth && p.PatternValue == dayOfMonth)))
            .Include(p => p.Activity).ThenInclude(a => a.Role)
            .Include(p => p.Activity).ThenInclude(a => a.Category)
            .Include(p => p.Importance)
            .ToListAsync(ct);

        var plannerCoveredKeys = new HashSet<(long, int, int)>();
        foreach (var p in plannerPatterns)
        {
            if (coveredActivityIds.Contains(p.ActivityId)) continue;

            result.Add(MapPatternToSuggestion(p, SuggestionSourceType.PlannedPattern));
            plannerCoveredKeys.Add((p.ActivityId, p.PatternType, p.PatternValue));
        }

        // 3. Activity-history-pattern suggestions
        var historyPatterns = await dbContext.Set<ActivityHistoryPattern>()
            .Where(p => p.UserId == userId &&
                ((p.PatternType == (int)RecurrenceType.DayOfWeek && p.PatternValue == isoDayOfWeek) ||
                 (p.PatternType == (int)RecurrenceType.DayOfMonth && p.PatternValue == dayOfMonth)))
            .Include(p => p.Activity).ThenInclude(a => a.Role)
            .Include(p => p.Activity).ThenInclude(a => a.Category)
            .ToListAsync(ct);

        foreach (var p in historyPatterns)
        {
            if (coveredActivityIds.Contains(p.ActivityId)) continue;
            if (plannerCoveredKeys.Contains((p.ActivityId, p.PatternType, p.PatternValue))) continue;

            result.Add(MapHistoryPatternToSuggestion(p));
        }

        await Send.OkAsync(result, ct);
    }

    private static SuggestionResponse MapPatternToSuggestion(PlannerTaskPattern p, SuggestionSourceType sourceType)
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

    private static SuggestionResponse MapHistoryPatternToSuggestion(ActivityHistoryPattern p)
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
                    Icon = activity.Category.Icon,
                    Role = null
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
