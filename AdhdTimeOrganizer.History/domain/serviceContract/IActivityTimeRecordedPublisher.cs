using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.History.domain.serviceContract;

/// <summary>
/// Announces newly committed history rows as <c>ActivityTimeRecordedEvent</c>s, so the completion automation
/// sees time logged by hand or by a timer the same way it sees desktop-tracked time (FO2).
/// <para>
/// An interface for the same reason as <see cref="ITimerBoundaryAlarmService"/>: the DI scan registers
/// marker-carrying services <c>AsImplementedInterfaces()</c>.
/// </para>
/// </summary>
public interface IActivityTimeRecordedPublisher
{
    /// <summary>
    /// Publishes for <paramref name="rows"/>, which must already be committed — the day totals are read back
    /// from the ledger. Never throws for a failure of its own: the rows are saved, and the caller's 201 must
    /// not turn into an error for work that succeeded.
    /// <para>
    /// <paramref name="countsAsRepetition"/> is the request's own flag: the rows are one sitting of the item they link,
    /// to be ticked once (<c>ActivityDayTotal.CountsAsRepetition</c>).
    /// </para>
    /// </summary>
    Task PublishAsync(DbContext db, long userId, IReadOnlyCollection<ActivityHistory> rows, bool countsAsRepetition, CancellationToken ct);
}
