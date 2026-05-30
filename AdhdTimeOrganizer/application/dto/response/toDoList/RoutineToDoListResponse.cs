using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.todoList;


namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record RoutineTodoListResponse : BaseTodoListResponse,
    IProjectionResponse<RoutineTodoListResponse, RoutineTodoList>
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public DateTime LastCompletedAt { get; init; }
    public IntTime? SuggestedTime { get; init; }
    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }

    public static IQueryable<RoutineTodoListResponse> Projection(IQueryable<RoutineTodoList> q) =>
        q.Select(e => new RoutineTodoListResponse
        {
            Id = e.Id,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                Role = new ActivityRoleResponse
                {
                    Id = e.Activity.Role.Id,
                    Name = e.Activity.Role.Name,
                    Text = e.Activity.Role.Text,
                    Color = e.Activity.Role.Color,
                    Icon = e.Activity.Role.Icon,
                },
                Category = e.Activity.Category == null ? null : new ActivityCategoryResponse
                {
                    Id = e.Activity.Category.Id,
                    Name = e.Activity.Category.Name,
                    Text = e.Activity.Category.Text,
                    Color = e.Activity.Category.Color,
                    Icon = e.Activity.Category.Icon,
                },
            },
            IsDone = e.IsDone,
            DisplayOrder = e.DisplayOrder,
            DoneCount = e.DoneCount,
            TotalCount = e.TotalCount,
            Note = e.Note,
            Steps = e.Steps.Select(s => new TodoListStepResponse
            {
                Id = s.Id,
                Name = s.Name,
                Order = s.Order,
                IsDone = s.IsDone,
                Note = s.Note,
            }).ToList(),
            RoutineTimePeriod = new RoutineTimePeriodResponse
            {
                Id = e.RoutineTimePeriod.Id,
                Text = e.RoutineTimePeriod.Text,
                Color = e.RoutineTimePeriod.Color,
                LengthInDays = e.RoutineTimePeriod.LengthInDays,
                IsHidden = e.RoutineTimePeriod.IsHidden,
                ResetAnchorDay = e.RoutineTimePeriod.ResetAnchorDay,
                StreakThreshold = e.RoutineTimePeriod.StreakThreshold,
                StreakGraceDays = e.RoutineTimePeriod.StreakGraceDays,
                Streak = e.RoutineTimePeriod.Streak,
                BestStreak = e.RoutineTimePeriod.BestStreak,
                LastResetAt = e.RoutineTimePeriod.LastResetAt,
                HistoryDepth = e.RoutineTimePeriod.HistoryDepth,
                // NextResetAt omitted (computed, not on entity)
                // CompletionHistory omitted
            },
            Streak = e.Streak,
            BestStreak = e.BestStreak,
            LastCompletedAt = e.LastCompletedAt,
            SuggestedTime = e.SuggestedTime,
            SuggestedDays = e.SuggestedDays,
            SuggestedDayOfMonth = e.SuggestedDayOfMonth,
        });

    public static RoutineTodoListResponse FromEntity(RoutineTodoList e) => new()
    {
        Id = e.Id,
        Activity = new ActivityResponse
        {
            Id = e.Activity.Id,
            Name = e.Activity.Name,
            Text = e.Activity.Text,
            IsUnavoidable = e.Activity.IsUnavoidable,
            Role = new ActivityRoleResponse
            {
                Id = e.Activity.Role.Id,
                Name = e.Activity.Role.Name,
                Text = e.Activity.Role.Text,
                Color = e.Activity.Role.Color,
                Icon = e.Activity.Role.Icon,
            },
            Category = e.Activity.Category == null ? null : new ActivityCategoryResponse
            {
                Id = e.Activity.Category.Id,
                Name = e.Activity.Category.Name,
                Text = e.Activity.Category.Text,
                Color = e.Activity.Category.Color,
                Icon = e.Activity.Category.Icon,
            },
        },
        IsDone = e.IsDone,
        DisplayOrder = e.DisplayOrder,
        DoneCount = e.DoneCount,
        TotalCount = e.TotalCount,
        Note = e.Note,
        Steps = e.Steps.Select(s => new TodoListStepResponse
        {
            Id = s.Id,
            Name = s.Name,
            Order = s.Order,
            IsDone = s.IsDone,
            Note = s.Note,
        }).ToList(),
        RoutineTimePeriod = new RoutineTimePeriodResponse
        {
            Id = e.RoutineTimePeriod.Id,
            Text = e.RoutineTimePeriod.Text,
            Color = e.RoutineTimePeriod.Color,
            LengthInDays = e.RoutineTimePeriod.LengthInDays,
            IsHidden = e.RoutineTimePeriod.IsHidden,
            ResetAnchorDay = e.RoutineTimePeriod.ResetAnchorDay,
            StreakThreshold = e.RoutineTimePeriod.StreakThreshold,
            StreakGraceDays = e.RoutineTimePeriod.StreakGraceDays,
            Streak = e.RoutineTimePeriod.Streak,
            BestStreak = e.RoutineTimePeriod.BestStreak,
            LastResetAt = e.RoutineTimePeriod.LastResetAt,
            HistoryDepth = e.RoutineTimePeriod.HistoryDepth,
            CompletionHistory = e.RoutineTimePeriod.CompletionHistoryColl
                .Select(c => new PeriodCompletionRecord(c.PeriodStart, c.PeriodEnd, c.CompletedCount, c.TotalCount))
                .ToList(),
        },
        Streak = e.Streak,
        BestStreak = e.BestStreak,
        LastCompletedAt = e.LastCompletedAt,
        SuggestedTime = e.SuggestedTime,
        SuggestedDays = e.SuggestedDays,
        SuggestedDayOfMonth = e.SuggestedDayOfMonth,
    };
}
