using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public record TwoFactorAuthResponse : IMyResponse
{
    public required bool TwoFactorEnabled { get; init; }
    public string? QrCode { get; init; }
    public IEnumerable<string>? RecoveryCodes { get; init; }
}