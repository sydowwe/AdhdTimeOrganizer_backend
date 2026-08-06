namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Names of the non-standard claims this framework mints into its access tokens. Constants because
/// the writer (<c>JwtService.CreateUserClaims</c>) and the readers (authorization policies and
/// handlers, in this assembly and in hosts) must agree exactly: a typo on either side does not fail
/// the build, it silently changes who is allowed in.
/// </summary>
public static class AuthClaims
{
    /// <summary>How the session was authenticated — <c>AuthMethodEnum</c> as a string.</summary>
    public const string AuthMethod = "auth_method";

    /// <summary>Which kind of client the token was minted for — <c>ClientTypeEnum</c> as a string.</summary>
    public const string ClientType = "client_type";

    /// <summary><see cref="ClientType"/> value identifying a token client (browser extension, desktop, mobile).</summary>
    public const string ExtensionClientType = nameof(@enum.ClientTypeEnum.Extension);
}