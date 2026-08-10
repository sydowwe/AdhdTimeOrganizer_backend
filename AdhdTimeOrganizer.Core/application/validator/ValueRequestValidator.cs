using AdhdTimeOrganizer.Core.application.dto.request.generic;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Core.application.validator;

public class ValueRequestValidator : Validator<ValueRequest>
{
    public ValueRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty();
    }
}