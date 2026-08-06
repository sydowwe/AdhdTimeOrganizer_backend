namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Development fixtures that are not scoped to a single user — either genuinely global demo data,
/// or data spanning several users that the seeder picks itself (typically through
/// <see cref="ISeedUserProvider"/>). Never runs outside development.
/// <para>
/// Free to truncate, and the manager will call <see cref="TruncateTable"/> first when asked to
/// override — fixtures have no production rows to protect.
/// </para>
/// </summary>
public interface IAppWideDevSeeder : IAppWideSeeder
{
    /// <summary>
    /// Wipes this seeder's table. Called by the manager in reverse <see cref="IDatabaseSeeder.Order"/>
    /// before seeding, so FK children go first. Use
    /// <c>dbContext.TruncateTableCascadeAsync&lt;TEntity&gt;()</c>.
    /// </summary>
    Task TruncateTable();

    /// <summary>
    /// Seeds the fixture data. Should no-op when its table is already populated, so that seeding
    /// without <c>overrideData</c> is safe to repeat.
    /// </summary>
    Task Seed();
}