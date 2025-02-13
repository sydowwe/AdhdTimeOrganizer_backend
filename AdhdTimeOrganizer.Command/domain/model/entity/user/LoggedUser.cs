namespace AdhdTimeOrganizer.Command.domain.model.entity.user;

public class LoggedUser
{
    public long UserId { get; set; }
    public string Email { get; set; }
    public IEnumerable<string> Roles { get; set; }
}