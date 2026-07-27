using AdhdTimeOrganizer.application.dto.request.generic;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class GetByDateCalendarValidator : Validator<ValueRequest>
{
    public GetByDateCalendarValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty()
            .Must(d => DateOnly.TryParseExact(d, "dd-MM-yyyy", out _))
            .WithMessage("Date must be in dd-MM-yyyy format.");
    }
}