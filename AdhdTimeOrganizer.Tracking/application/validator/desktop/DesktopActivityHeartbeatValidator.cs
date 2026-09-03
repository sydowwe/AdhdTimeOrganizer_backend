using System.Text;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class DesktopActivityHeartbeatValidator : Validator<DesktopActivityWindowDto>
{
    public DesktopActivityHeartbeatValidator()
    {
        RuleFor(x => x.WindowStart)
            .Must(w => w.Second == 0 && w.Millisecond == 0)
            .WithMessage("WindowStart must be aligned to a full minute boundary.");


        RuleFor(x => x.Entries).NotEmpty();

        // A heartbeat covers one minute, so the entry count is bounded by how many windows a person can
        // touch in that minute — see TrackingIngestLimits.
        RuleFor(x => x.Entries)
            .Must(entries => entries.Count <= TrackingIngestLimits.MaxDesktopEntriesPerWindow)
            .WithMessage($"A heartbeat may carry at most {TrackingIngestLimits.MaxDesktopEntriesPerWindow} entries.")
            .When(x => x.Entries is not null);

        // Two rules, not a chain: a .Must(...).Must(...).WithMessage(...) attaches the message to the
        // second call only, so the per-entry cap used to fail with FluentValidation's default text (CQ-20).
        RuleFor(x => x.Entries)
            .Must(entries => entries.All(e => e.ActiveSeconds + e.BackgroundSeconds <= 60))
            .WithMessage("No entry may report more than 60 seconds of active and background time together.")
            .When(x => x.Entries is { Count: > 0 });

        RuleFor(x => x.Entries)
            .Must(entries => entries.Sum(e => e.ActiveSeconds) <= 60)
            .WithMessage("Total active seconds across all entries must not exceed 60.")
            .When(x => x.Entries is { Count: > 0 });

        RuleForEach(x => x.Entries).ChildRules(e =>
        {
            // The four lengths are the entry columns' widths — see TrackingIngestLimits (CQ-9).
            e.RuleFor(x => x.ProcessName).NotEmpty().MaximumLength(TrackingIngestLimits.MaxProcessNameLength);
            e.RuleFor(x => x.WindowTitle).NotEmpty().MaximumLength(TrackingIngestLimits.MaxWindowTitleLength);
            e.RuleFor(x => x.ProductName).MaximumLength(TrackingIngestLimits.MaxProductNameLength);
            e.RuleFor(x => x.ExecutablePath).MaximumLength(TrackingIngestLimits.MaxExecutablePathLength);

            // The column widths above are not sufficient on their own: both of these sit in a btree
            // index whose tuple is limited in bytes, not characters.
            e.RuleFor(x => x)
                .Must(x => Encoding.UTF8.GetByteCount(x.ProcessName) + Encoding.UTF8.GetByteCount(x.WindowTitle)
                           <= TrackingIngestLimits.MaxIndexedEntryTextBytes)
                .WithMessage(
                    $"ProcessName and WindowTitle together must be at most {TrackingIngestLimits.MaxIndexedEntryTextBytes} bytes when UTF-8 encoded.")
                .OverridePropertyName("WindowTitle")
                .When(x => x.ProcessName is not null && x.WindowTitle is not null);

            e.RuleFor(x => x.ActiveSeconds).GreaterThanOrEqualTo(0);
            e.RuleFor(x => x.BackgroundSeconds).GreaterThanOrEqualTo(0);
            e.RuleFor(x => x)
                .Must(x => x.ActiveSeconds > 0 || x.BackgroundSeconds > 0)
                .WithMessage("Either ActiveSeconds or BackgroundSeconds must be greater than 0.");
        });
    }
}