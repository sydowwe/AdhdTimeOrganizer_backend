using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record LoginRequest(
    [ Required] string RecaptchaToken,
    [ Required] string Timezone,
    bool StayLoggedIn = false
) : IMyRequest;