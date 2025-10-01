using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ChangeEmailRequest : VerifyUserRequest
{
    [Required] public required string NewEmail { get; init; }
}