namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Production data owned by one user — the rows a brand-new account needs to be usable (default
/// categories, presets, settings). Runs on sign-up for the new user, and can be replayed for one
/// user or for every user.
/// <para>
/// Like <see cref="IAppWideDefaultSeeder"/> this never truncates; it is a per-user upsert. Two
/// methods rather than one <c>Seed(overrideData)</c> because resetting has to be able to report
/// that it <i>couldn't</i> map its defaults onto the user's existing rows — see
/// <see cref="ResetDefaults"/>.
/// </para>
/// </summary>
public interface IPerUserDefaultSeeder : IPerUserSeeder
{
    /// <summary>
    /// Inserts whatever defaults this user is missing. Must be idempotent — it runs on sign-up and
    /// again on every replay.
    /// </summary>
    /// <param name="userId">The user to seed data for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetupDefaults(long userId, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the user's existing default rows in place to match the current definitions, keeping
    /// their ids so anything referencing them survives.
    /// </summary>
    /// <param name="userId">The user to reset data for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>false</c> when the user's rows can't be matched up to the defaults (usually a different
    /// row count — the user edited or deleted some), meaning nothing was reset. The manager then
    /// falls through to <see cref="SetupDefaults"/> rather than forcing a rewrite.
    /// </returns>
    Task<bool> ResetDefaults(long userId, CancellationToken ct = default);
}