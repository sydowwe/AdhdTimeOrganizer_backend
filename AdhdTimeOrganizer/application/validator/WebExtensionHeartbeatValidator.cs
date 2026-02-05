using AdhdTimeOrganizer.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionHeartbeatValidator : Validator<WebExtensionHeartbeatRequest>
{
    public WebExtensionHeartbeatValidator()
    {
        RuleFor(x => x.HeartbeatAt)
            .NotEmpty()
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5));

        RuleFor(x => x.Window).NotNull();
        RuleFor(x => x.Window.WindowMinutes).Equal(5);
        RuleFor(x => x.Window.Activities).NotNull();

        RuleForEach(x => x.Window.Activities).ChildRules(a =>
        {
            a.RuleFor(x => x.Domain).NotEmpty().MaximumLength(255);
            a.RuleFor(x => x.Url).MaximumLength(2048);
            a.RuleFor(x => x.ActiveSeconds).InclusiveBetween(0, 300);
            a.RuleFor(x => x.BackgroundSeconds).InclusiveBetween(0, 300);
        });
    }
}