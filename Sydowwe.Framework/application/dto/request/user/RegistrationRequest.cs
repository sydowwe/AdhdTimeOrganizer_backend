using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// The fields every sign-up carries, whatever proves the identity — password, Google, or anything
/// added later. Each host derives a concrete request per method (adding a password, an OAuth subject
/// id, a recaptcha token) and implements its own <c>ToEntity</c>, because only the host knows its
/// concrete user type.
/// </summary>
public record RegistrationRequest
{
    public required string Email { get; set; }

    public required bool TwoFactorEnabled { get; init; }

    /// <summary>
    /// Captured at sign-up rather than first login because the confirmation email goes out
    /// immediately — before the user can possibly have logged in — and has to be in their language.
    /// </summary>
    public required AvailableLocales CurrentLocale { get; init; }

    /// <summary>
    /// IANA id, resolved by the host into whatever its user type stores. Also a sign-up field rather
    /// than a login one: anything scheduled for a new account (reminders, digests, routine resets)
    /// can fire before the first login, and would fire at the wrong local time without it.
    /// </summary>
    public required string Timezone { get; init; }

    /// <summary>
    /// Copies the fields that live on <see cref="BaseUser"/> onto a user the caller constructed, so a
    /// host's <c>ToEntity</c> only has to deal with its own additions. Returns the same instance for
    /// use as an expression.
    /// </summary>
    protected TUser PopulateBaseFields<TUser>(TUser user) where TUser : BaseUser
    {
        user.Email = Email;
        user.TwoFactorEnabled = TwoFactorEnabled;
        user.Locale = CurrentLocale;
        user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
        return user;
    }
}