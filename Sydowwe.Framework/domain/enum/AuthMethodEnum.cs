namespace Sydowwe.Framework.domain.@enum;

/// <summary>
/// How a session was originally authenticated. Persisted on <c>refresh_token.auth_method</c> as an
/// <b>int</b>, so the numeric values are a storage contract — append new members, never reorder.
/// </summary>
public enum AuthMethodEnum
{
    Password = 0,
    Microsoft = 1,

    /// <summary>Google OAuth sign-in. Appended (rather than inserted next to <see cref="Microsoft"/>)
    /// to keep the existing stored ordinals stable.</summary>
    Google = 2
}