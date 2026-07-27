using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.generic;

namespace Sydowwe.Framework.application.validator;

public class IdListRequestValidator : Validator<IdListRequest>
{
    public IdListRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithMessage("At least one id must be provided.");
    }
}