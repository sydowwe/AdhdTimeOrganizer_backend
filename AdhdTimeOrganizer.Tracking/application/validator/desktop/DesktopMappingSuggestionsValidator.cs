using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class DesktopMappingSuggestionsValidator : Validator<DesktopMappingSuggestionsRequest>
{
    /// <summary>
    /// The read is one indexed slice per timer interval, so the window bounds the work. Ninety days is past the
    /// point where an old habit says much about which process is which activity today.
    /// </summary>
    public const int MaxDays = 90;

    public DesktopMappingSuggestionsValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(1, MaxDays);
    }
}
