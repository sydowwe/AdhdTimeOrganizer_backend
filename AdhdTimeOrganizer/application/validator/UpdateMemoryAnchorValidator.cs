using AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateMemoryAnchorValidator : Validator<MemoryAnchorRequest>
{
    public UpdateMemoryAnchorValidator()
    {
        RuleFor(x => x.AnchorMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.AnchorYear).InclusiveBetween(1900, 2200);
        RuleFor(x => x.Rating).InclusiveBetween(1, 10);
        RuleFor(x => x.HighlightNote).NotEmpty().MaximumLength(1000);
    }
}
