namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Identity of a seeder — nothing else. Deliberately carries no seeding entry point: the four
/// seeder kinds take genuinely different arguments (app-wide vs per-user, override vs truncate),
/// so hoisting a single <c>Seed()</c> up here would force meaningless stubs onto most seeders.
/// <para>
/// Pick the leaf interface that describes the data, along two axes:
/// <list type="bullet">
/// <item><b>Scope</b> — <see cref="IAppWideSeeder"/> (one dataset for the whole app) vs
/// <see cref="IPerUserSeeder"/> (one dataset per user).</item>
/// <item><b>Purpose</b> — <c>…DefaultSeeder</c> (production data, seeded in every environment,
/// upserts in place) vs <c>…DevSeeder</c> (fixtures, development only, may truncate).</item>
/// </list>
/// Giving: <see cref="IAppWideDefaultSeeder"/>, <see cref="IPerUserDefaultSeeder"/>,
/// <see cref="IAppWideDevSeeder"/>, <see cref="IPerUserDevSeeder"/>.
/// </para>
/// </summary>
public interface IDatabaseSeeder
{
    /// <summary>
    /// Gets the name of the seeder, used for logging and for targeting a single seeder by name.
    /// </summary>
    string SeederName { get; }

    /// <summary>
    /// Gets the execution order priority (lower numbers execute first). Truncation runs in the
    /// reverse order, so FK dependencies only have to be expressed once.
    /// </summary>
    int Order { get; }
}

/// <summary>
/// Marker for seeders whose data is not owned by any single user — roles, the root admin, lookup
/// tables shared by everyone. Their entry point takes no user id.
/// </summary>
public interface IAppWideSeeder : IDatabaseSeeder;

/// <summary>
/// Marker for seeders that produce one dataset per user. Their entry point takes the user id;
/// a manager decides which users to run them for.
/// </summary>
public interface IPerUserSeeder : IDatabaseSeeder;