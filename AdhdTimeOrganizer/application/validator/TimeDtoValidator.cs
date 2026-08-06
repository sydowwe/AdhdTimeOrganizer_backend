using AdhdTimeOrganizer.application.dto.dto;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.validator;

public class TimeDtoValidator : Validator<TimeDto>
{
    public TimeDtoValidator()
    {
        RuleFor(x => x.Hours).InclusiveBetween(0, 23);
        RuleFor(x => x.Minutes).InclusiveBetween(0, 59);
    }
}