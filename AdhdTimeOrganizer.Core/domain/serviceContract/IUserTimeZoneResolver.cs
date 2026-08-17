namespace AdhdTimeOrganizer.Core.domain.serviceContract;

/// <summary>
/// Hands an endpoint the zone a user's wall-clock times are expressed in, so that a bare
/// <c>TimeDto</c> / <c>DateOnly</c> off a request can be resolved to an instant.
///
/// <para>Lives in Core because every slice needs it and no slice can see <c>User</c>'s owner otherwise. The
/// implementation is scoped and memoises per request: a dashboard endpoint resolves the zone once no matter
/// how many ranges it converts, and the extra round trip is one indexed primary-key read.</para>
/// </summary>
public interface IUserTimeZoneResolver
{
    /// <summary>
    /// The user's configured zone, or <see cref="TimeZoneInfo.Utc"/> when the row is gone. UTC is the
    /// deliberate fallback rather than <see cref="TimeZoneInfo.Local"/>: a missing user must not make the
    /// answer depend on which machine the server happens to be running on.
    /// </summary>
    ValueTask<TimeZoneInfo> GetAsync(long userId, CancellationToken ct = default);
}
