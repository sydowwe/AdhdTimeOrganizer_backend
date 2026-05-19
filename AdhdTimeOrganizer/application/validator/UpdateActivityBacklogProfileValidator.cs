using AdhdTimeOrganizer.application.dto.request.activity.profile;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateActivityBacklogProfileValidator : Validator<ActivityBacklogProfileRequest>
{
    public UpdateActivityBacklogProfileValidator()
    {
        RuleFor(x => x.MinParticipants).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(x => x.MinParticipants).When(x => x.MaxParticipants.HasValue);
        RuleFor(x => x.DurationMinutes).GreaterThan(0);
    }
}
