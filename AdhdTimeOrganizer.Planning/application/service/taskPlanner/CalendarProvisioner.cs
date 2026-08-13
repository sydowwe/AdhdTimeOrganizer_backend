using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.service.taskPlanner;

/// <inheritdoc cref="ICalendarProvisioner"/>
public sealed class CalendarProvisioner(DbContext dbContext, ILogger<CalendarProvisioner> logger)
    : ICalendarProvisioner, IScopedService
{
    public async Task<Calendar> EnsureForDateAsync(long userId, DateOnly date, CancellationToken ct = default)
    {
        if (await FindAsync(userId, date, ct) is { } existing)
            return existing;

        var calendar = CalendarDayFactory.Create(userId, date);
        dbContext.Set<Calendar>().Add(calendar);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // (user_id, date) is unique, so two writes racing onto the same unplanned day — a phone and a
            // desktop both starting the morning's first task — leave one of them here. The row it wanted now
            // exists, so the loser reads it rather than failing a write the user has every right to make.
            // Detaching first: a failed insert stays Added on the tracker and would be retried by the next
            // SaveChanges on this request, which is the endpoint's own write.
            dbContext.Entry(calendar).State = EntityState.Detached;

            var winner = await FindAsync(userId, date, ct);
            if (winner is null)
                throw;

            logger.LogDebug("Lost the race creating a calendar for user {UserId} on {Date}; using the existing row",
                userId, date);
            return winner;
        }

        logger.LogInformation("Created a calendar for user {UserId} on {Date} on first use", userId, date);
        return calendar;
    }

    /// <summary>
    /// Scoped by hand rather than leaning on the host's global query filter, for the same reason
    /// <c>CalendarSeeder</c> does: this is told which user to act for, and the answer must not depend on who
    /// happens to be signed in. Filtered, a caller acting for another user would read no row and try to insert
    /// a duplicate onto (user_id, date).
    /// </summary>
    private Task<Calendar?> FindAsync(long userId, DateOnly date, CancellationToken ct) =>
        dbContext.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, ct);
}
