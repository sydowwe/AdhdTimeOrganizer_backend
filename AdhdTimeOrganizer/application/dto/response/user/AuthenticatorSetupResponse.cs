namespace AdhdTimeOrganizer.application.dto.response.user;

public record AuthenticatorSetupResponse
{
    public required string QrCode { get; set; }
    public required List<string> RecoveryCodes { get; set; }
}