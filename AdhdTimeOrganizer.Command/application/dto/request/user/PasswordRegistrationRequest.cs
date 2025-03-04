using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record PasswordRegistrationRequest : UserRequest
{
    [Required] public required string Password { get; set; }
}