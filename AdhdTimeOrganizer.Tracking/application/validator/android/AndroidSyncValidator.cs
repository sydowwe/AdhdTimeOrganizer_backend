using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class AndroidSyncValidator : Validator<AndroidSyncRequest>
{
    public AndroidSyncValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(TrackingIngestLimits.MaxAndroidDeviceIdLength);
        RuleFor(x => x.SyncedUpToUtc).NotEmpty();
        RuleFor(x => x.Sessions).NotNull();

        // See TrackingIngestLimits: android_session_data is append-only and a session's length drives an
        // allocation per minute in the unified loader, so both the batch and each session are bounded here.
        RuleFor(x => x.Sessions)
            .Must(s => s.Count <= TrackingIngestLimits.MaxAndroidSessionsPerSync)
            .WithMessage($"A sync may carry at most {TrackingIngestLimits.MaxAndroidSessionsPerSync} sessions.")
            .When(x => x.Sessions is not null);

        RuleForEach(x => x.Sessions).ChildRules(s =>
        {
            s.RuleFor(x => x.PackageName).NotEmpty().MaximumLength(TrackingIngestLimits.MaxAndroidPackageNameLength);
            s.RuleFor(x => x.AppLabel).NotEmpty().MaximumLength(TrackingIngestLimits.MaxAndroidAppLabelLength);
            // Bounded as well as non-empty because the endpoint logs both verbatim when they do not parse
            // (CQ-22) — they are unvalidated free text on a per-request path.
            s.RuleFor(x => x.SessionStartUtc).NotEmpty().MaximumLength(TrackingIngestLimits.MaxInstantStringLength);
            s.RuleFor(x => x.SessionEndUtc).NotEmpty().MaximumLength(TrackingIngestLimits.MaxInstantStringLength);
            s.RuleFor(x => x.DurationSeconds)
                .GreaterThan(0)
                .LessThanOrEqualTo(TrackingIngestLimits.MaxAndroidSessionSeconds);
        });
    }
}
