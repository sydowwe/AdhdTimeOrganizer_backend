using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record ChangePasswordRequest(
    string? TwoFactorAuthToken,
    [ Required] string CurrentPassword,
    [ Required] string NewPassword
) : IMyRequest;