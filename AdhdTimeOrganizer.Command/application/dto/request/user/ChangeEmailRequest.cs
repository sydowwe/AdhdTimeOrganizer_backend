using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record ChangeEmailRequest(
    string? TwoFactorAuthToken,
    string Password,
    [ Required] string NewEmail
) : VerifyUserRequest(TwoFactorAuthToken, Password);