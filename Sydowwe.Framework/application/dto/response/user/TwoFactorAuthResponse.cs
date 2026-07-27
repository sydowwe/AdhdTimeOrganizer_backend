using Sydowwe.Framework.application.dto.response.@base;

namespace Sydowwe.Framework.application.dto.response.user;

public record TwoFactorAuthResponse : IMyResponse
{
    public required bool TwoFactorEnabled { get; init; }
    public string? QrCode { get; init; }
    public IEnumerable<string>? RecoveryCodes { get; init; }
}