namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary><c>POST /welcome-back/fresh-start/undo</c>: the token the fresh start returned.</summary>
public record UndoFreshStartRequest
{
    public required string UndoToken { get; init; }
}
