using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class AndroidMappingSuggestionsValidator : Validator<AndroidMappingSuggestionsRequest>
{
    /// <summary>The same window bound as the desktop read, for the same reason.</summary>
    public AndroidMappingSuggestionsValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(1, DesktopMappingSuggestionsValidator.MaxDays);
    }
}
