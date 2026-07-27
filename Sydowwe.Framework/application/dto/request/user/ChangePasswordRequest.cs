namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// Inherits <c>Password</c> and <c>TwoFactorAuthToken</c> from <see cref="VerifyUserRequest"/>:
/// changing a password is a step-up operation, so the caller re-proves identity rather than merely
/// holding a session. The current password therefore arrives as <c>Password</c> and is verified by
/// the step-up pre-processor before the handler runs.
/// </summary>
public record ChangePasswordRequest : VerifyUserRequest
{
    public required string NewPassword { get; set; }
}