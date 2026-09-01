using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

/// <summary>
/// The validators for the four batched calendar entry points, grouped because they share one rule that is the
/// whole reason they are strict: <b>the id list must be distinct</b>.
/// <para>
/// Every batch answers with one result per requested id, in request order and of the same length, so the
/// caller can attribute a failure to a day. A duplicate id makes that mapping ambiguous — dedupe and the
/// lengths stop matching, keep it and the day is written twice — so it is rejected outright rather than
/// resolved with a guess. FastEndpoints resolves validators by exact request type, so each concrete request
/// needs its own class here; a shared abstract base would never be discovered.
/// </para>
/// </summary>
public class CalendarTaskSummariesValidator : Validator<CalendarTaskSummariesRequest>
{
    public CalendarTaskSummariesValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Until >= x.From)
            .WithMessage("Until must be on or after From.");

        RuleFor(x => x)
            .Must(x => x.Until.DayNumber - x.From.DayNumber + 1 <= PlannerBatchLimits.MaxSummaryRangeDays)
            .WithMessage($"The range may not exceed {PlannerBatchLimits.MaxSummaryRangeDays} days.")
            .When(x => x.Until >= x.From);
    }
}

public class ApplyTemplateToCalendarsBatchValidator : Validator<ApplyTemplateToCalendarsBatchRequest>
{
    public ApplyTemplateToCalendarsBatchValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.ConflictResolution).IsInEnum();

        RuleFor(x => x.CalendarIds).ApplyCalendarIdListRules();

        RuleFor(x => x.TasksFromTemplate).NotNull().NotEmpty();

        RuleForEach(x => x.TasksFromTemplate)
            .ChildRules(task =>
            {
                task.RuleFor(t => t.StartTime).ApplyTimeOfDayRules();
                task.RuleFor(t => t.EndTime).ApplyTimeOfDayRules();

                task.RuleFor(t => t).Must(t => t.StartTime.IsBefore(t.EndTime))
                    .WithMessage("StartTime must be before EndTime.");

                task.RuleFor(t => t.ActivityId).GreaterThan(0);
                task.RuleFor(t => t.ImportanceId).GreaterThan(0).When(t => t.ImportanceId.HasValue);
            })
            .When(x => x.TasksFromTemplate != null);
    }
}

public class CopyPlannerTasksToDaysValidator : Validator<CopyPlannerTasksToDaysRequest>
{
    public CopyPlannerTasksToDaysValidator()
    {
        RuleFor(x => x.SourceCalendarId).GreaterThan(0);

        RuleFor(x => x.TargetCalendarIds).ApplyCalendarIdListRules();

        // Copying a day onto itself would duplicate every one of its tasks in place. The action bar cannot
        // produce it (the source day is not selectable as a target), so it is a malformed request, not a
        // no-op worth silently succeeding.
        RuleFor(x => x)
            .Must(x => x.TargetCalendarIds == null || !x.TargetCalendarIds.Contains(x.SourceCalendarId))
            .WithMessage("TargetCalendarIds must not contain SourceCalendarId.");
    }
}

public class BatchUpdateCalendarDayTypeValidator : Validator<BatchUpdateCalendarDayTypeRequest>
{
    public BatchUpdateCalendarDayTypeValidator()
    {
        RuleFor(x => x.DayType).IsInEnum();
        RuleFor(x => x.CalendarIds).ApplyCalendarIdListRules();
    }
}

internal static class CalendarIdListRules
{
    /// <summary>Non-empty, positive, distinct, and bounded — see <see cref="PlannerBatchLimits"/>.</summary>
    public static IRuleBuilderOptions<T, List<long>> ApplyCalendarIdListRules<T>(this IRuleBuilder<T, List<long>> rule) =>
        rule.NotNull()
            .Must(ids => ids is { Count: > 0 })
            .WithMessage("At least one calendar id is required.")
            .Must(ids => ids == null || ids.Count <= PlannerBatchLimits.MaxCalendarIds)
            .WithMessage($"At most {PlannerBatchLimits.MaxCalendarIds} calendar ids may be sent in one request.")
            .Must(ids => ids == null || ids.All(id => id > 0))
            .WithMessage("Calendar ids must be positive.")
            .Must(ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Calendar ids must be distinct — the response answers one result per requested id, in order.");
}
