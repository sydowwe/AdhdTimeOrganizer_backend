using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class DesktopActivityHeartbeatValidator : Validator<DesktopActivityWindowDto>
{
    public DesktopActivityHeartbeatValidator()
    {
        RuleFor(x => x.WindowStart)
            .Must(w => w.Second == 0 && w.Millisecond == 0)
            .WithMessage("WindowStart must be aligned to a full minute boundary.");


        RuleFor(x => x.Entries).NotEmpty();

        RuleFor(x => x.Entries)
            .Must(entries => entries.All(e => e.ActiveSeconds + e.BackgroundSeconds <= 60))
            .Must(entries => entries.Sum(e => e.ActiveSeconds) <= 60)
            .WithMessage("Total seconds across all entries must not exceed 60.")
            .When(x => x.Entries is { Count: > 0 });

        RuleForEach(x => x.Entries).ChildRules(e =>
        {
            e.RuleFor(x => x.ProcessName).NotEmpty();
            e.RuleFor(x => x.WindowTitle).NotEmpty();
            e.RuleFor(x => x.ActiveSeconds).GreaterThanOrEqualTo(0);
            e.RuleFor(x => x.BackgroundSeconds).GreaterThanOrEqualTo(0);
            e.RuleFor(x => x)
                .Must(x => x.ActiveSeconds > 0 || x.BackgroundSeconds > 0)
                .WithMessage("Either ActiveSeconds or BackgroundSeconds must be greater than 0.");
        });
    }
}
