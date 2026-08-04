namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>
/// The VAPID application server key the browser needs for <c>PushManager.subscribe()</c>.
/// <c>PublicKey</c> is null when the deployment has no VAPID credentials configured — the SPA
/// should then skip Web Push setup entirely rather than call subscribe with a missing key.
/// </summary>
public record VapidPublicKeyResponse(string? PublicKey);
