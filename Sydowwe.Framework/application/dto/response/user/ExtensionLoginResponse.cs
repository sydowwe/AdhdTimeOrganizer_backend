namespace Sydowwe.Framework.application.dto.response.user;

/// <summary>
/// Token-client counterpart of <see cref="LoginResponse"/>. Where the web flow writes the session to
/// cookies and answers with the flags alone, this hands the tokens back in the body.
///
/// <para>All three are nullable because exactly one group is populated per reply:
/// <see cref="PendingAuthToken"/> when a second factor is still due, otherwise the
/// access/refresh pair.</para>
/// </summary>
public record ExtensionLoginResponse : LoginResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? PendingAuthToken { get; init; }
}