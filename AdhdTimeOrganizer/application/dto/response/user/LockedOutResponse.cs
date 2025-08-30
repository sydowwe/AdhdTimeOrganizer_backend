namespace AdhdTimeOrganizer.application.dto.response.user;

public record LockedOutResponse
{
    public required int Seconds { get; init; }
    public static string Status => "User locked out";
}