using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;
public record ResetPasswordRequest(
    [ Required] long UserId,
    [ Required] string Token,
    [ Required] string NewPassword
) : IMyRequest;