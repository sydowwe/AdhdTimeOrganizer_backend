using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class WebExtensionHeartbeatValidator : Validator<WebExtensionHeartbeatRequest>
{
    public WebExtensionHeartbeatValidator()
    {
        RuleFor(x => x.HeartbeatAt)
            .NotEmpty();

        RuleFor(x => x.WindowMinutes).Equal(1);

        // WindowStart must sit exactly on a minute boundary. The timeline endpoint treats each row as
        // covering [WindowStart, WindowStart + 1min) and joins adjacent windows by equality, so an
        // off-boundary value corrupts the rendered timeline silently instead of failing. See the
        // invariant documented on WebExtensionActivityEntry.WindowStart.
        RuleFor(x => x.WindowStart)
            .NotEmpty()
            .Must(x => x.Ticks % TimeSpan.TicksPerMinute == 0)
            .WithMessage("WindowStart must be aligned to a whole minute (zero seconds and sub-second component).");
        RuleFor(x => x.Activities).NotNull();

        RuleForEach(x => x.Activities).ChildRules(a =>
        {
            a.RuleFor(x => x.Domain).NotEmpty().MaximumLength(255);
            a.RuleFor(x => x.Url).MaximumLength(2048);
            a.RuleFor(x => x.ActiveSeconds).InclusiveBetween(0, 60);
            a.RuleFor(x => x.BackgroundSeconds).InclusiveBetween(0, 60);
        });
    }
}