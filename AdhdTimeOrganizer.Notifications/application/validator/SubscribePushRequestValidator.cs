using AdhdTimeOrganizer.Notifications.application.dto;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Notifications.application.validator;

public class SubscribePushRequestValidator : Validator<SubscribePushRequest>
{
    public SubscribePushRequestValidator()
    {
        RuleFor(x => x.Endpoint)
            .NotEmpty()
            .Must(v => Uri.TryCreate(v, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Endpoint must be a valid https URL.");

        RuleFor(x => x.P256dh)
            .NotEmpty();

        RuleFor(x => x.Auth)
            .NotEmpty();
    }
}