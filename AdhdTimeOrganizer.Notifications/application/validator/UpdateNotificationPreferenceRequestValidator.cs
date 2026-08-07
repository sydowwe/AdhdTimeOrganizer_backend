using AdhdTimeOrganizer.Notifications.application.dto;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Notifications.application.validator;

public class UpdateNotificationPreferenceRequestValidator : Validator<UpdateNotificationPreferenceRequest>
{
    public UpdateNotificationPreferenceRequestValidator()
    {
        RuleFor(x => x.Type)
            .Must(v => Enum.IsDefined(typeof(NotificationType), v))
            .WithMessage("Invalid NotificationType value.");

        RuleFor(x => x.Channel)
            .Must(v => Enum.IsDefined(typeof(NotificationChannel), v))
            .WithMessage("Invalid NotificationChannel value.");
    }
}