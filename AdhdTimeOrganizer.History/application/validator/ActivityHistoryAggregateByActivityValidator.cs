using AdhdTimeOrganizer.History.application.dto.request.history;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

public class ActivityHistoryAggregateByActivityValidator : Validator<ActivityHistoryAggregateByActivityRequest>
{
    /// <summary>
    /// The caller batches one rendered to-do list per request; a few hundred ids is far past any real
    /// list, and the cap is what keeps an unbounded id list from turning into an unbounded IN (…).
    /// </summary>
    public const int MaxActivityIds = 500;

    public ActivityHistoryAggregateByActivityValidator()
    {
        RuleFor(x => x.ActivityIds)
            .NotNull();

        RuleFor(x => x.ActivityIds.Count)
            .LessThanOrEqualTo(MaxActivityIds)
            .WithMessage($"At most {MaxActivityIds} activity ids may be requested at once.");

        RuleForEach(x => x.ActivityIds)
            .GreaterThan(0);
    }
}
