using AdhdTimeOrganizer.application.dto.request.history;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ActivityHistoryValidator : Validator<ActivityHistoryRequest>
{
    public ActivityHistoryValidator()
    {
        RuleFor(x => x.ActivityId)
            .NotEmpty();

        RuleFor(x => x.StartTimestamp)
            .NotEmpty();

        RuleFor(x => x.Length)
            .NotNull()
            .Must(x => x.TotalSeconds >= 0)
            .WithMessage("Length must be non-negative.");
    }
}
