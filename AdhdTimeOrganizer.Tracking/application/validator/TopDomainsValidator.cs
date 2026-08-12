using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class TopDomainsValidator : Validator<SummaryCardsRequest>
{
    public TopDomainsValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty();
        RuleFor(x => x.TopN).InclusiveBetween(1, 50).When(x => x.TopN.HasValue);
        RuleFor(x => x.Baseline).IsInEnum();
    }
}