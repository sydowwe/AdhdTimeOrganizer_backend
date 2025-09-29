using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UserRequest : EmailRequest, IUpdateRequest
{
    public required bool TwoFactorEnabled { get; init; }
    [Required] public required AvailableLocales CurrentLocale { get; init; }
    [Required] public required string Timezone { get; init; }
}