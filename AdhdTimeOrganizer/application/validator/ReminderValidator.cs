using AdhdTimeOrganizer.application.dto.request.reminder;
using AdhdTimeOrganizer.application.service.reminder;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ReminderValidator : Validator<ReminderRequest>
{
    public ReminderValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .When(x => x.Note != null);

        RuleFor(x => x.PlannerTaskId)
            .GreaterThan(0)
            .When(x => x.PlannerTaskId.HasValue);

        // The registry rejects a positive offset outright, so catching it here turns a 500 into a message the
        // user can act on. "10 minutes before" is -10, never 10.
        // Every offset rule below is skipped when the field is absent — that is the "use my default" signal,
        // resolved server-side after validation, not a value to check.
        RuleFor(x => x.LeadOffsetsMinutes)
            .Must(offsets => offsets!.All(o => o <= 0))
            .When(x => x.LeadOffsetsMinutes is not null)
            .WithMessage("Lead offsets must be 0 or negative — use -10 for 'ten minutes before'.");

        RuleFor(x => x.LeadOffsetsMinutes)
            .Must(offsets => offsets!.Distinct().Count() == offsets!.Count)
            .When(x => x.LeadOffsetsMinutes is not null)
            .WithMessage("Lead offsets must be unique.");

        // A recurring reminder's offset is folded into the recurrence anchor, because the Kernel's recurring
        // schedule has no lead-offset concept. One offset is expressible; a second one would be dropped
        // silently, so it is refused instead. See ReminderRegistrationService.BuildSchedule.
        RuleFor(x => x.LeadOffsetsMinutes)
            .Must(offsets => offsets!.Distinct().Count() <= 1)
            .When(x => x.Recurrence.HasValue && x.LeadOffsetsMinutes is not null)
            .WithMessage("A repeating reminder supports a single lead offset.");

        // A one-shot reminder whose occurrences are all in the past would be accepted by the registry and
        // then never fire: the occurrence calculator only returns instants at or after now, deliberately, so
        // there is no backfill. Reject it rather than hand back a reminder that silently does nothing.
        // A *recurring* reminder anchored in the past is fine and common — a yearly birthday.
        // Checked against the *latest* occurrence (RemindAt + the largest offset, which is <= 0), not against
        // RemindAt itself: a reminder set only for "-10" is already spent ten minutes before its own instant.
        RuleFor(x => x)
            .Must(x => ReminderRegistrationService.AsUtc(x.RemindAt).AddMinutes(LargestOffset(x)) > DateTimeOffset.UtcNow)
            .When(x => !x.Recurrence.HasValue)
            .WithMessage("RemindAt must be in the future — a reminder for a past moment would never fire.")
            .OverridePropertyName(nameof(ReminderRequest.RemindAt));
    }

    /// <summary>
    /// The offset that fires last. Absent or empty means "at RemindAt", i.e. 0 — and since a resolved default
    /// is always negative, 0 is also the most permissive assumption for the past-instant check below.
    /// </summary>
    private static int LargestOffset(ReminderRequest request) => request.LeadOffsetsMinutes is { Count: > 0 } offsets ? offsets.Max() : 0;
}
