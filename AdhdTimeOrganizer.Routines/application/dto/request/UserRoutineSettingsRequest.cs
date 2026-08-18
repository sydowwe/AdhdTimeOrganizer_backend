using System.Globalization;

namespace AdhdTimeOrganizer.Routines.application.dto.request.todoList;

/// <summary>
/// Writes the routine-domain per-user settings. Every field is nullable and a <c>null</c> means "no value" —
/// so sending <c>null</c> for the dismissal is how the client un-dismisses the weekly review card.
/// </summary>
public record UserRoutineSettingsRequest
{
    /// <summary>
    /// The week-start the user just dismissed the weekly routine review for, or <c>null</c> to clear it.
    /// <para>
    /// A <c>string</c> parsed by <see cref="TryGetRoutineReviewDismissedForWeekStart"/> rather than a bound
    /// <see cref="DateOnly"/>, for the same reason as <c>SpendStreakFreezeRequest.PeriodStart</c>: a client
    /// that round-trips the value through a JavaScript <c>Date</c> sends back a full instant
    /// (<c>2026-08-17T00:00:00.000Z</c>). Both forms are accepted, and an instant is read as UTC so a
    /// server in a negative-offset zone cannot land a day earlier than the client meant.
    /// </para>
    /// </summary>
    public string? RoutineReviewDismissedForWeekStart { get; init; }

    /// <summary>Parses the field as either a plain date or a full ISO-8601 instant. <c>null</c> parses to null.</summary>
    public bool TryGetRoutineReviewDismissedForWeekStart(out DateOnly? weekStart)
    {
        if (string.IsNullOrWhiteSpace(RoutineReviewDismissedForWeekStart))
        {
            weekStart = null;
            return true;
        }

        if (TryParse(RoutineReviewDismissedForWeekStart, out var parsed))
        {
            weekStart = parsed;
            return true;
        }

        weekStart = null;
        return false;
    }

    /// <summary>The same parse, callable without a request instance — the validator needs it before the endpoint runs.</summary>
    public static bool TryParse(string? value, out DateOnly date)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
        {
            date = DateOnly.FromDateTime(instant);
            return true;
        }

        date = default;
        return false;
    }
}
