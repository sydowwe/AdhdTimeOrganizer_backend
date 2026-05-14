namespace AdhdTimeOrganizer.application.dto.request.user;

public record ChangePasswordRequest : VerifyUserRequest
{
    public required string NewPassword { get; set; }
}