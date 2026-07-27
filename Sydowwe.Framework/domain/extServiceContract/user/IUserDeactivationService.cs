namespace Sydowwe.Framework.domain.extServiceContract.user;

public interface IUserDeactivationService
{
    Task DeactivateUserAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// The inverse of <see cref="DeactivateUserAsync"/>: revives a previously deactivated login. Sets
    /// <c>IsActive = true</c>, forces a password change on next login (<c>MustChangePassword = true</c>),
    /// resets the password to a fresh temporary one and rotates the security stamp (invalidating any
    /// lingering cookies/credentials). Used by the employee re-hire (boomerang) flow. Returns the new
    /// temporary password to hand to the returning employee. Throws if the user does not exist or the
    /// Identity operation fails (the caller maps that to a 500 and rolls its transaction back).
    /// </summary>
    Task<string> ReactivateUserAsync(long userId, CancellationToken ct = default);
}