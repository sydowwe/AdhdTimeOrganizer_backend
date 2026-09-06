using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// Shared paging for the two nightly routine sweeps. Both used to materialise every user's periods — with
/// two levels of collection include in the reset job's case — in a single query, so peak memory scaled with
/// total users × routines × steps rather than with anything bounded.
/// <para>
/// <b>Paged by user, not by row.</b> A page must never split one user's periods across two batches: both
/// sweeps commit and notify per page, and a half-swept user would announce half their day. Skip/Take over
/// periods would also drift as the sweep mutates the rows it is ordering by; a user-id list read once up
/// front does not.
/// </para>
/// </summary>
internal static class RoutineSweepPaging
{
    /// <summary>
    /// The default users per page — small enough that a page's object graph is bounded, large enough that a
    /// normal-sized instance still sweeps in a handful of round trips. Both handlers take it as a defaulted
    /// constructor parameter so a test can cross a page boundary without seeding a hundred users.
    /// </summary>
    public const int UserPageSize = 100;

    /// <summary>
    /// Reads the distinct user ids the sweep will visit, in pages. <paramref name="source"/> must carry the
    /// same filter the caller pages with, so a user whose every period is out of scope is never paged at all.
    /// <para>
    /// Ordered so the pages partition the id space rather than overlapping — an unordered
    /// <c>Distinct</c> gives Postgres no reason to return the same order twice.
    /// </para>
    /// </summary>
    public static async Task<List<long[]>> LoadUserPagesAsync(
        IQueryable<RoutineTimePeriod> source, int pageSize, CancellationToken ct)
    {
        var userIds = await source
            .Select(p => p.UserId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

        return userIds.Chunk(pageSize).ToList();
    }
}
