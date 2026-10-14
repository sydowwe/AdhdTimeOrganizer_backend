using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.timeAway.command;

/// <summary>
/// <c>DELETE /time-away/{id}</c>. The stretch's days are un-stamped in the same save as the row is removed: a day
/// still of the stretch's kind returns to its natural type, the stretch's location leaves the days that still carry
/// it, and a day the user overrode keeps its override. <b>Nothing else is undone</b> — no task is touched, and the
/// routine cycles the stretch excused stay excused (owner decision, 2026-10-12): Routines only ever re-scores
/// upwards, and the event below reaches it for the newly un-away days with nothing to raise.
/// </summary>
public class DeleteTimeAwayEndpoint(DbContext dbContext) : BaseDeleteEndpoint<TimeAway>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private IReadOnlyList<DateOnly> _changedDates = [];

    protected override async Task BeforeDeleteAsync(TimeAway entity, CancellationToken ct = default) =>
        _changedDates = await TimeAwayStamper.StageRemovalAsync(_dbContext, entity, ct);

    /// <summary>
    /// After the commit, for FO6's sake as much as for symmetry: the days are no longer away. FO6 brings nothing
    /// back (a day it passed over stays decided), and Routines leaves excused cycles excused.
    /// </summary>
    protected override async Task AfterSave(TimeAway entity, CancellationToken ct = default)
    {
        if (_changedDates.Count > 0)
            await new CalendarDayTypesChangedEvent(entity.UserId, _changedDates).PublishAsync(Mode.WaitForAll, ct);
    }
}
