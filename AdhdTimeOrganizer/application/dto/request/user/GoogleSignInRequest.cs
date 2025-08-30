using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record GoogleSignInRequest : LoginRequest
{
    [Required]
    public string Code { get; init; }
}