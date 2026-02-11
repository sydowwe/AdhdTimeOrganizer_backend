using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class PieChartValidator : Validator<PieChartRequest>
{
    public PieChartValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty();
        RuleFor(x => x.MinPercent).InclusiveBetween(0.1, 50.0).When(x => x.MinPercent.HasValue);
    }
}
