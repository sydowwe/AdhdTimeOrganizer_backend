using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class AndroidAppDetailsValidator : Validator<AndroidAppDetailsRequest>
{
    public AndroidAppDetailsValidator()
    {
        this.ApplyDetailsDateRangeRules();
        RuleFor(x => x.PackageName).NotEmpty().MaximumLength(TrackingIngestLimits.MaxAndroidPackageNameLength);
    }
}
