using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ChangeEmailRequest : VerifyUserRequest
{
    [Required] public string NewEmail { get; init; }
}