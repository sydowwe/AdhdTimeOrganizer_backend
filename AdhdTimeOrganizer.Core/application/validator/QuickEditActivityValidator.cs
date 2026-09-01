using AdhdTimeOrganizer.Core.application.dto.request.activity;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.Core.application.validator;

public class QuickEditActivityValidator : Validator<QuickEditActivityRequest>
{
    public QuickEditActivityValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EntityFieldLengths.Name);
        RuleFor(x => x.Text).MaximumLength(EntityFieldLengths.Text).When(x => x.Text != null);
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId.HasValue);
    }
}