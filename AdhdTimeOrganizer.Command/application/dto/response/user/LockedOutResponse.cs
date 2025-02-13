namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class LockedOutResponse
{
    public string Status { get; } = "lockedOut";
    public int Seconds { get; set; }
}