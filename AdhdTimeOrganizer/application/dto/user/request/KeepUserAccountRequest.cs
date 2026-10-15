namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary><c>POST /auth/account/keep</c> (P3-07): the user id and token from the "keep my account" link.</summary>
public record KeepUserAccountRequest
{
    public required long UserId { get; init; }
    public required string Token { get; init; }
}
