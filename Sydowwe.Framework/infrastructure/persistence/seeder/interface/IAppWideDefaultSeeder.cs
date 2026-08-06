namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Production data that is not owned by any user and must exist in <b>every</b> environment —
/// user roles, the root admin account, app-level lookup tables.
/// <para>
/// Runs in production, so it never truncates: <paramref name="overrideData"/> means "bring existing
/// rows up to date in place", not "wipe and re-insert". Truncating <c>user_role</c> or <c>user</c>
/// would cascade away every user↔role assignment. If you want wipe-and-reinsert semantics, the data
/// is a fixture — implement <see cref="IAppWideDevSeeder"/> instead.
/// </para>
/// </summary>
public interface IAppWideDefaultSeeder : IAppWideSeeder
{
    /// <summary>
    /// Seeds the app-wide default data. Must be idempotent — it runs on every startup.
    /// </summary>
    /// <param name="overrideData">
    /// When <c>true</c>, update rows that already exist to match the seeded definition. When
    /// <c>false</c>, leave existing rows untouched and only insert what is missing.
    /// </param>
    Task Seed(bool overrideData = false);
}