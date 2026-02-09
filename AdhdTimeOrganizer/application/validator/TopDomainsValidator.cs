using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TopDomainsValidator : Validator<TopDomainsRequest>
{
    public TopDomainsValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        RuleFor(x => x.To).LessThanOrEqualTo(x => x.From.AddDays(31))
            .WithMessage("Maximum range is 31 days");
        RuleFor(x => x.TopN).InclusiveBetween(1, 50).When(x => x.TopN.HasValue);
        RuleFor(x => x.MinPercent).InclusiveBetween(0.1, 50).When(x => x.MinPercent.HasValue);
        RuleFor(x => x.Baseline).IsInEnum();

        // Cannot specify both TopN and MinPercent
        RuleFor(x => x)
            .Must(x => !(x.TopN.HasValue && x.MinPercent.HasValue))
            .WithMessage("Cannot specify both TopN and MinPercent");
    }
}
