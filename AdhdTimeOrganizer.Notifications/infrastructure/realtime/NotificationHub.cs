using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AdhdTimeOrganizer.Notifications.infrastructure.realtime;

/// <summary>
/// Live notification channel. Clients connect authenticated; the dispatcher targets
/// individual users via Clients.User(userId), keyed on Context.UserIdentifier (the
/// NameIdentifier claim — same value as ClaimsPrincipal.GetId()). The server only pushes
/// ("ReceiveNotification"); clients do not invoke hub methods in v1.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public const string ReceiveMethod = "ReceiveNotification";
}