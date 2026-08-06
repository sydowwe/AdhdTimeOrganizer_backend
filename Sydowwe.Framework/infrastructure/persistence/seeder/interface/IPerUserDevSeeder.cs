namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Development fixtures owned by one user — the demo activities, todo lists and history that make a
/// dev account look lived-in. Never runs outside development.
/// <para>
/// The manager decides whose account gets them (usually the root admin) and, when overriding,
/// truncates first. Unlike <see cref="IPerUserDefaultSeeder"/> these are not part of a new user's
/// baseline — sign-up must not run them.
/// </para>
/// </summary>
public interface IPerUserDevSeeder : IPerUserSeeder
{
    /// <summary>
    /// Wipes this seeder's table for all users. Called by the manager in reverse
    /// <see cref="IDatabaseSeeder.Order"/> before seeding, so FK children go first. Use
    /// <c>dbContext.TruncateTableCascadeAsync&lt;TEntity&gt;()</c>.
    /// </summary>
    Task TruncateTable();

    /// <summary>
    /// Seeds the fixture data for one user, stamping <c>UserId</c> on every row it inserts.
    /// </summary>
    /// <param name="userId">The user the fixtures belong to.</param>
    Task SeedForUser(long userId);
}