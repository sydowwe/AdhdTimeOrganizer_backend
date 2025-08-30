using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record TwoFactorAuthResponse : IMyResponse
{
    public required bool TwoFactorEnabled { get; init; }
    public string? QrCode { get; init; }
    public IEnumerable<string>? RecoveryCodes { get; init; }
}