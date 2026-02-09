using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class DomainDetailsValidator : Validator<DomainDetailsRequest>
{
    public DomainDetailsValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        RuleFor(x => x.To).LessThanOrEqualTo(x => x.From.AddDays(31))
            .WithMessage("Maximum range is 31 days");
        RuleFor(x => x.Domain).NotEmpty().MaximumLength(255);
    }
}
