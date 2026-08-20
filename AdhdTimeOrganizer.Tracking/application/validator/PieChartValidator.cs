using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class PieChartValidator : Validator<PieChartRequest>
{
    public PieChartValidator()
    {
        this.ApplyDateRangeRules();
        RuleFor(x => x.MinPercent).InclusiveBetween(0.1, 50.0).When(x => x.MinPercent.HasValue);
    }
}
