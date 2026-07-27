namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>Payload from the frontend after PushManager.subscribe().</summary>
public class SubscribePushRequest
{
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? UserAgent { get; set; }
}

public class UnsubscribePushRequest
{
    public required string Endpoint { get; set; }
}