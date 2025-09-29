using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record LoginRequest : IMyRequest
{
    public bool StayLoggedIn { get; set; }
    [Required] public required string RecaptchaToken { get; init; }
    [Required] public required string Timezone { get; init; }
}