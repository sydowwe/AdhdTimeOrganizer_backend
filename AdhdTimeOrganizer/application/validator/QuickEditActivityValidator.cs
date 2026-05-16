using AdhdTimeOrganizer.application.dto.request.activity;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class QuickEditActivityValidator : Validator<QuickEditActivityRequest>
{
    public QuickEditActivityValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Text).MaximumLength(500).When(x => x.Text != null);
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId.HasValue);
    }
}
