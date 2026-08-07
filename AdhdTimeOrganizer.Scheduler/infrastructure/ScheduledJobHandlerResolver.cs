using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The single place handler keys are matched against the registered <see cref="IScheduledJobHandler"/>
/// implementations. Every resolve / existence check (registration validation, dispatch, replay) goes
/// through here so the <b>duplicate-key</b> case is detected loudly instead of one handler being picked
/// silently while its twin is dead — a latent misconfiguration that grows likelier as more modules
/// migrate handlers. Two implementations sharing a <see cref="IScheduledJobHandler.Key"/> is always a DI
/// wiring bug, so it fails fast.
/// </summary>
internal static class ScheduledJobHandlerResolver
{
    /// <summary>
    /// Returns the one handler registered for <paramref name="key"/>, or <c>null</c> if none is.
    /// Throws <see cref="InvalidOperationException"/> if two or more share the key.
    /// </summary>
    public static IScheduledJobHandler? Resolve(IEnumerable<IScheduledJobHandler> handlers, string key)
    {
        // Deduplicate by concrete type first: broad assembly scans (AddCore + host-level scan) can
        // register the same implementation class twice against IScheduledJobHandler. Two registrations
        // of the same type are a DI scanning artifact, not a real key conflict.
        IScheduledJobHandler? found = null;
        foreach (var handler in handlers.DistinctBy(h => h.GetType()))
        {
            if (handler.Key != key)
                continue;
            if (found is not null)
                throw new InvalidOperationException(
                    $"Multiple IScheduledJobHandler implementations are registered for key '{key}' " +
                    $"({found.GetType().Name}, {handler.GetType().Name}); handler keys must be unique.");
            found = handler;
        }

        return found;
    }

    /// <summary>True if exactly one handler is registered for <paramref name="key"/>; throws on a duplicate.</summary>
    public static bool Exists(IEnumerable<IScheduledJobHandler> handlers, string key) => Resolve(handlers, key) is not null;
}