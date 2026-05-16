using AdhdTimeOrganizer.application.dto.request.user;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ConnectGoogleCalendarValidator : Validator<ConnectGoogleCalendarRequest>
{
    public ConnectGoogleCalendarValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(512);
    }
}
