using System.Collections.Concurrent;
using System.Reflection;

namespace MojaDigitalnaFirma.Kernel.notification.payload;

/// <summary>
/// Binds an <see cref="INotificationPayload"/> record to the <see cref="NotificationType"/> it renders as, so
/// the typed <c>NotifyAsync</c> overload derives the type from the payload and a producer cannot pair the
/// wrong two. Declared as an attribute rather than an interface property so the binding never leaks into the
/// persisted JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class NotificationPayloadAttribute(NotificationType type) : Attribute
{
    public NotificationType Type { get; } = type;
}

/// <summary>Resolves the <see cref="NotificationType"/> a payload record declares. Cached — reflection runs once per type.</summary>
public static class NotificationPayloadTypes
{
    private static readonly ConcurrentDictionary<Type, NotificationType> Cache = new();

    /// <exception cref="InvalidOperationException">The payload type carries no <see cref="NotificationPayloadAttribute"/>.</exception>
    public static NotificationType Of(INotificationPayload payload) => Of(payload.GetType());

    /// <inheritdoc cref="Of(INotificationPayload)"/>
    public static NotificationType Of(Type payloadType)
    {
        return Cache.GetOrAdd(payloadType, static t =>
            t.GetCustomAttribute<NotificationPayloadAttribute>()?.Type
            ?? throw new InvalidOperationException(
                $"{t.Name} implements {nameof(INotificationPayload)} but carries no [{nameof(NotificationPayloadAttribute)}]; " +
                "the notification kind cannot be derived. Add the attribute or use the explicit-type NotifyAsync overload."));
    }
}