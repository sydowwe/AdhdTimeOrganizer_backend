namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// The planner's notification producer: the one place in this slice that names a planner <c>NotificationType</c>
/// payload sent straight through <c>INotificationService</c> (the automatic block reminder goes through the
/// Reminders module instead, from <c>ReminderRegistrationService</c>).
/// <para>
/// Every method is best-effort and returns whether the notification actually went out, so a caller that stamps a
/// "notified" mark off the result never suppresses one that did not.
/// </para>
/// </summary>
public interface IPlannerNotificationService
{
    /// <summary>
    /// "<paramref name="count"/> of today's blocks slipped by — re-plan the rest of today?" One notification for
    /// however many blocks the caller found. Nothing is sent for a count of zero.
    /// </summary>
    /// <param name="firstTaskId">The earliest of the blocks, which the notification links to.</param>
    Task<bool> NotifyBlocksMissedAsync(long userId, DateOnly date, int count, long firstTaskId, CancellationToken ct = default);

    /// <summary>"Time's up for <paramref name="activityName"/> — wrap up or extend?"</summary>
    Task<bool> NotifyBlockOverrunAsync(long userId, long plannerTaskId, string? activityName, int minutesOver, CancellationToken ct = default);
}
