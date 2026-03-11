using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class AndroidSyncValidator : Validator<AndroidSyncRequest>
{
    public AndroidSyncValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SyncedUpToUtc).NotEmpty();
        RuleFor(x => x.Sessions).NotNull();

        RuleForEach(x => x.Sessions).ChildRules(s =>
        {
            s.RuleFor(x => x.PackageName).NotEmpty().MaximumLength(255);
            s.RuleFor(x => x.AppLabel).NotEmpty().MaximumLength(255);
            s.RuleFor(x => x.SessionStartUtc).NotEmpty();
            s.RuleFor(x => x.SessionEndUtc).NotEmpty();
            s.RuleFor(x => x.DurationSeconds).GreaterThan(0);
        });
    }
}
