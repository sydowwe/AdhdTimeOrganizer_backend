using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record GoogleAuthRegistrationRequest : UserRequest
{
    [Required] public required string GoogleOAuthUserId { get; init; }
}