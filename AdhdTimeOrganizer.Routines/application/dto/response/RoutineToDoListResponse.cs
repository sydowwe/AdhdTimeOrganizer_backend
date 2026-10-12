using System.Linq.Expressions;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.Routines.application.dto.response.todoList;

public record RoutineTodoListResponse : BaseTodoListResponse,
    IProjectionResponse<RoutineTodoListResponse, RoutineTodoList>
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public DateTime LastCompletedAt { get; init; }

    // SuggestedTime is inherited from BaseTodoListResponse — it was declared here while the to-do
    // response had none, which is exactly how GET /api/todo-list-item came to omit it entirely.

    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }

    /// <summary>
    /// F2-06: how long one occurrence of this item usually takes — the median of the time logged against the item per
    /// cycle of its period, over its newest timed cycles. <c>null</c> below
    /// <c>EstimateFitRule.RoutineItemMinimumSamples</c> occurrences. Information only: it never rewrites
    /// <c>SuggestedTime</c>, which also decides tracker auto-completion. Not part of <see cref="Projection"/> — every
    /// read fills it afterwards through <c>RoutineItemTypicalDurationFiller</c>.
    /// </summary>
    public long? TypicalSeconds { get; init; }

    /// <summary>The occurrences behind <see cref="TypicalSeconds"/>; <c>null</c> whenever that is.</summary>
    public int? SampleCount { get; init; }

    public static IQueryable<RoutineTodoListResponse> Projection(IQueryable<RoutineTodoList> q)
    {
        return q.Select(ProjectionExpression);
    }

    /// <summary>
    /// Maps an entity the caller already holds — the grouped read materialises the whole graph, so it must not
    /// pay for the expression tree to be interpreted once per item. Requires <c>Activity</c>, its <c>Role</c>
    /// and <c>Steps</c> to be loaded; <see cref="Projection"/> is the path for anything still in the database.
    /// </summary>
    public static RoutineTodoListResponse From(RoutineTodoList entity) => Map(entity);

    private static readonly Expression<Func<RoutineTodoList, RoutineTodoListResponse>> ProjectionExpression =
        e => new RoutineTodoListResponse
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
                    Icon = e.Activity.Role.Icon
                },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = e.Activity.Category.Id,
                        Name = e.Activity.Category.Name,
                        Text = e.Activity.Category.Text,
                        Color = e.Activity.Category.Color,
                        Icon = e.Activity.Category.Icon
                    }
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
                Note = s.Note
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
                // Every column RoutineTimePeriodResponse holds, or the nested period lies rather than being
                // merely incomplete: the freeze fields default to 0, and 0 is a real value the client reads as
                // "no freezes left" — see the contract on RoutineTimePeriodResponse.FreezesRemaining.
                ReminderLeadDays = e.RoutineTimePeriod.ReminderLeadDays,
                FreezesRemaining = e.RoutineTimePeriod.FreezesRemaining,
                FreezeBudget = e.RoutineTimePeriod.FreezeBudget,
                FreezeBudgetResetsAt = e.RoutineTimePeriod.FreezeBudgetResetsAt
                // NextResetAt and CompletionHistory stay omitted — neither is computable in SQL. A caller that
                // serves this nested period on its own must fill them through
                // RoutineTimePeriodReadRefresher, which is what the two flat reads do.
            },
            Streak = e.Streak,
            BestStreak = e.BestStreak,
            LastCompletedAt = e.LastCompletedAt,
            SuggestedTime = e.SuggestedTime,
            SuggestedDays = e.SuggestedDays,
            SuggestedDayOfMonth = e.SuggestedDayOfMonth
        };

    /// <summary>
    /// The same expression compiled once, behind <see cref="From"/>. Declared after
    /// <see cref="ProjectionExpression"/> because static field initializers run in textual order.
    /// </summary>
    private static readonly Func<RoutineTodoList, RoutineTodoListResponse> Map = ProjectionExpression.Compile();
}