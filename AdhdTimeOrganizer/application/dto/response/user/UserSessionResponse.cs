namespace AdhdTimeOrganizer.application.dto.response.user;

public record UserSessionResponse
{
    public required long Id { get; init; }
    public required string Device { get; init; }
    public required string Browser { get; init; }
    public string? Ip { get; init; }
    public required DateTime LastUsedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public bool IsCurrent { get; init; }
}
