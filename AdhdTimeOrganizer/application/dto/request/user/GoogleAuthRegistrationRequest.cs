using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record GoogleAuthRegistrationRequest : UserRequest
{
    [Required] public required string GoogleOAuthUserId { get; init; }
}