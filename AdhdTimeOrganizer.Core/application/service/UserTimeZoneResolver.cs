using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Core.application.service;

/// <inheritdoc cref="IUserTimeZoneResolver"/>
public class UserTimeZoneResolver(DbContext dbContext) : IUserTimeZoneResolver, IScopedService
{
    // Keyed by user id rather than held as a single field: a request is one user in every path that exists
    // today, but a background handler resolving several users through one scope must not be handed the
    // first one's zone for all of them.
    private readonly Dictionary<long, TimeZoneInfo> _cache = new();

    public async ValueTask<TimeZoneInfo> GetAsync(long userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;

        // IgnoreQueryFilters: the global IEntityWithUser filter does not cover User itself today, but this
        // read must not start depending on that — it is the one read that has to succeed for a user before
        // anything else about them can be scoped correctly.
        var timeZone = await dbContext.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

        _cache[userId] = timeZone;
        return timeZone;
    }
}
