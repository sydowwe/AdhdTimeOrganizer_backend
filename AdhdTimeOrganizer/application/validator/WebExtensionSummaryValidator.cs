using AdhdTimeOrganizer.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionSummaryValidator : Validator<WebExtensionSummaryRequest>
{
    public WebExtensionSummaryValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        RuleFor(x => x.To).LessThanOrEqualTo(x => x.From.AddDays(7));
        RuleFor(x => x.WindowMinutes)
            .Must(x => x == null || new[] { 5, 10, 15, 30, 60 }.Contains(x.Value))
            .WithMessage("WindowMinutes must be 5, 10, 15, 30, or 60");
    }
}