using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// A sign-up that proves identity with a local password. Adds the two fields the password flow needs
/// on top of <see cref="RegistrationRequest"/>, and the host's mapping to its own user type.
///
/// <para><see cref="RecaptchaToken"/> is required, not optional: <c>/auth/register</c> is anonymous
/// and rate-limited only per IP, so a bot farm can create accounts faster than the throttle notices.
/// Every host on this framework verifies a captcha there — that is why the field lives here rather
/// than on each host's own request.</para>
///
/// <para>Hosts derive a concrete record per sign-up method and implement <see cref="ToEntity"/>,
/// because only the host knows its concrete user type. Use
/// <see cref="RegistrationRequest.PopulateBaseFields{TUser}"/> so the override only deals with its
/// own additions.</para>
/// </summary>
public abstract record BasePasswordRegistrationRequest<TUser> : RegistrationRequest
    where TUser : BaseUser
{
    public required string Password { get; set; }

    public required string RecaptchaToken { get; init; }

    public abstract TUser ToEntity { get; }
}