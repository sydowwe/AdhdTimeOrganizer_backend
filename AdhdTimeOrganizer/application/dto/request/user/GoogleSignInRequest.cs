using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record GoogleSignInRequest : ICreateRequest
{
    [Required] public required string Timezone { get; init; }

    [Required]
    public required string Code { get; init; }
}