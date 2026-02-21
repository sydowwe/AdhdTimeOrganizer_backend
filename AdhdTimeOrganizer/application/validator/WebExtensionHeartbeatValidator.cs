using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.request.activityTracking.heartbeat;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionHeartbeatValidator : Validator<WebExtensionHeartbeatRequest>
{
    public WebExtensionHeartbeatValidator()
    {
        RuleFor(x => x.HeartbeatAt)
            .NotEmpty();

        RuleFor(x => x.WindowMinutes).Equal(1);
        RuleFor(x => x.Activities).NotNull();

        RuleForEach(x => x.Activities).ChildRules(a =>
        {
            a.RuleFor(x => x.Domain).NotEmpty().MaximumLength(255);
            a.RuleFor(x => x.Url).MaximumLength(2048);
            a.RuleFor(x => x.ActiveSeconds).InclusiveBetween(0, 60);
            a.RuleFor(x => x.BackgroundSeconds).InclusiveBetween(0, 60);
        });
    }
}