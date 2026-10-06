using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.serviceContract;

/// <summary>
/// Pre-reads the away days (vacation, sick) a reset needs, so the pure <c>RoutineResetService</c> can apply
/// <c>RoutineAwayRule</c> without touching the database. See <c>RoutineAwayDaysReader</c>.
/// </summary>
public interface IRoutineAwayDaysReader
{
    /// <summary>
    /// The away days, per user, covering every cycle a reset at <paramref name="now"/> would close among
    /// <paramref name="periods"/> — from the start of the oldest due cycle up to today. A user with no away day
    /// in that range is absent; look a user up with <c>GetValueOrDefault</c> and pass the (possibly null) set on.
    /// Reads nothing when no period is due.
    /// </summary>
    Task<IReadOnlyDictionary<long, IReadOnlySet<DateOnly>>> ForDueCyclesAsync(
        DbContext dbContext, IEnumerable<RoutineTimePeriod> periods, DateTime now, CancellationToken ct);
}
