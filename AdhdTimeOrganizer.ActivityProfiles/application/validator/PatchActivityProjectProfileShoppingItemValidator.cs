using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

/// <summary>
/// Shape only. Whether the item is the caller's is the endpoint's <c>BeforeMapping</c>, because it needs the
/// ownership seam and answers 404 rather than 400.
/// </summary>
public class PatchActivityProjectProfileShoppingItemValidator : Validator<PatchActivityProjectProfileShoppingItemRequest>
{
    public PatchActivityProjectProfileShoppingItemValidator()
    {
        RuleFor(x => x.TodoListItemId).GreaterThan(0);
    }
}
