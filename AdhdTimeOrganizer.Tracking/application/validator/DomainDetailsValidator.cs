using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class DomainDetailsValidator : Validator<DomainDetailsRequest>
{
    public DomainDetailsValidator()
    {
        this.ApplyDetailsDateRangeRules();
        RuleFor(x => x.Domain).NotEmpty().MaximumLength(255);
    }
}
