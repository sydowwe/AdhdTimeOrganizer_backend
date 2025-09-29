using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record PasswordRegistrationRequest : UserRequest
{
    [Required] public required string RecaptchaToken { get; init; }
    [Required] public required string Password { get; set; }
}