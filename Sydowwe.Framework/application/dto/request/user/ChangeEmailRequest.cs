namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// Inherits <c>Password</c> and <c>TwoFactorAuthToken</c> from <see cref="VerifyUserRequest"/>:
/// changing the email is a step-up operation, so the caller re-proves identity rather than merely
/// holding a session.
/// </summary>
public record ChangeEmailRequest : VerifyUserRequest
{
    public required string NewEmail { get; init; }
}