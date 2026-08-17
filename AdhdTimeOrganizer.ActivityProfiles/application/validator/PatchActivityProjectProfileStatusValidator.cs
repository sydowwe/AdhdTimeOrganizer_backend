using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class PatchActivityProjectProfileStatusValidator : Validator<PatchActivityProjectProfileStatusRequest>
{
    public PatchActivityProjectProfileStatusValidator()
    {
        RuleFor(x => x.ReadinessStatus).IsInEnum();
    }
}
