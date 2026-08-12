using System.Globalization;

namespace AdhdTimeOrganizer.Routines.application.dto.request.todoList;

/// <summary>
/// Spends one streak freeze on an elapsed period.
/// </summary>
public record SpendStreakFreezeRequest
{
    /// <summary>The period being covered. Bound from the route segment of the same name.</summary>
    public long TimePeriodId { get; init; }

    /// <summary>
    /// Which elapsed period to cover, matching the <c>periodStart</c> of a completion entry the client was
    /// already served.
    /// <para>
    /// Deliberately a <c>string</c> parsed by <see cref="TryGetPeriodStart"/> rather than a bound
    /// <see cref="DateOnly"/>: the field is a date on the wire, but a client that round-trips it through a
    /// JavaScript <c>Date</c> sends back a full instant (<c>2026-03-01T00:00:00.000Z</c>), which a
    /// <c>DateOnly</c> binder rejects outright. Accepting both and taking the date part is the difference
    /// between a working action and a 400 nobody can read.
    /// </para>
    /// </summary>
    public required string PeriodStart { get; init; }

    /// <summary>Parses <see cref="PeriodStart"/> as either a plain date or a full ISO-8601 instant.</summary>
    public bool TryGetPeriodStart(out DateOnly periodStart) => TryParse(PeriodStart, out periodStart);

    /// <summary>
    /// The same parse, callable without a request instance — the validator needs it before the endpoint runs.
    /// </summary>
    public static bool TryParse(string? value, out DateOnly periodStart)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out periodStart))
            return true;

        // AdjustToUniversal so an offset-bearing instant is read the same way the server stored it — the
        // completion rows are written from UTC.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
        {
            periodStart = DateOnly.FromDateTime(instant);
            return true;
        }

        periodStart = default;
        return false;
    }
}
