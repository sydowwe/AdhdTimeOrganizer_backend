using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class TwoFactorAuthResponse : IMyResponse
{
    public bool TwoFactorEnabled { get; set; }
    public string? QrCode { get; set; }
    public IEnumerable<string>? RecoveryCodes { get; set; }
}