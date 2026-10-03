using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class DeleteActivityHistoryEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityHistory>(dbContext)
{
    /// <summary>
    /// Deleting a session's last row deletes the session. Marked here so it goes in the base's one
    /// <c>SaveChanges</c> with the row — a session left with no rows is invisible to every read anyway, but it
    /// would still be the user's data sitting in a table.
    /// </summary>
    protected override async Task BeforeDeleteAsync(ActivityHistory entity, CancellationToken ct = default)
    {
        if (entity.SessionId is not { } sessionId)
            return;

        var hasOtherRows = await dbContext.Set<ActivityHistory>()
            .AnyAsync(h => h.SessionId == sessionId && h.Id != entity.Id, ct);
        if (hasOtherRows)
            return;

        var session = await dbContext.Set<ActivityHistorySession>().FindAsync([sessionId], ct);
        if (session != null)
            dbContext.Set<ActivityHistorySession>().Remove(session);
    }
}
