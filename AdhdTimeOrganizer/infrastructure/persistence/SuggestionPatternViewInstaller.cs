using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence;

/// <summary>
/// Creates the three suggestion-pattern materialized views if the database does not have them yet.
/// </summary>
/// <remarks>
/// The views are hand-written SQL in <c>infrastructure/persistence/sqlScripts/</c>, mapped with
/// <c>ToView</c> and therefore never emitted by a migration — so nothing created them in a real
/// database, only the integration-test fixture did. <see cref="interceptors.SuggestionPatternRefreshInterceptor"/>
/// issues a REFRESH on every save touching <c>PlannerTask</c>, <c>ActivityHistory</c> or
/// <c>Calendar</c>, which fails with 42P01 when the view is missing — that is what made per-user
/// Calendar seeding (and sign-up) blow up at startup.
///
/// The scripts are <c>&lt;EmbeddedResource&gt;</c> in the csproj, read via
/// <see cref="Assembly.GetManifestResourceStream(string)"/> — same arrangement as the Framework email
/// templates, so there is no copy-to-output step and no working-directory assumption. A new script in
/// that folder is picked up by the glob and installed here automatically.
///
/// The three views are created strictly in alphabetical-by-resource-name order with no explicit
/// dependency declaration — keep them mutually independent (none may reference another) or give a
/// dependent view an explicit ordering (e.g. a numeric filename prefix).
///
/// The check-then-create sequence is protected by a session-level Postgres advisory lock
/// (<see cref="AdvisoryLockKey"/>) held for the whole method, so two instances booting concurrently
/// serialize instead of both racing to create the same view and one crashing on a duplicate-object
/// error.
/// </remarks>
public static class SuggestionPatternViewInstaller
{
    private const string ResourceFolder = ".infrastructure.persistence.sqlScripts.";

    // Arbitrary fixed key identifying this installer's advisory lock; must stay stable across deploys.
    private const long AdvisoryLockKey = 84200001;

    public static async Task EnsureViewsCreatedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var assembly = typeof(SuggestionPatternViewInstaller).Assembly;
        var resourcePrefix = $"{assembly.GetName().Name}{ResourceFolder}";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order()
            .ToList();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({AdvisoryLockKey})", ct);

            foreach (var resource in resources)
            {
                var viewName = ViewNameOf(resource);

                var exists = await db.Database
                    .SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", QualifiedName(db, viewName))
                    .SingleAsync(ct);

                if (exists)
                    continue;

                await using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(ct);

                await db.Database.ExecuteSqlRawAsync(sql, ct);
                logger.LogInformation("Created missing materialized view {ViewName}.", viewName);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})", ct);
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The view's schema-qualified name, taken from the entity that maps it with <c>ToView</c>.
    /// </summary>
    /// <remarks>
    /// Read off the model rather than written down here, so the existence probe and the mapping
    /// cannot disagree: if EF reads the view from <c>planning</c>, this looks for it in
    /// <c>planning</c>. It used to say <c>public</c> outright, which stopped being true the moment
    /// each module got its own schema — and the failure is nasty rather than obvious. A probe against
    /// the wrong schema never finds the view, so the installer runs <c>CREATE MATERIALIZED VIEW</c>
    /// again on every single boot and dies on a duplicate-object error, holding the advisory lock.
    /// <para>
    /// A script with no entity mapping it falls back to the model's default schema. That is the old
    /// behaviour and the only guess available — nothing in the script's name says where it lives.
    /// </para>
    /// </remarks>
    private static string QualifiedName(AppDbContext db, string viewName)
    {
        var schema = db.Model.GetEntityTypes()
                         .FirstOrDefault(e => e.GetViewName() == viewName)
                         ?.GetViewSchema()
                     ?? db.Model.GetDefaultSchema();

        return schema is null ? viewName : $"{schema}.{viewName}";
    }

    // "AdhdTimeOrganizer.infrastructure.persistence.sqlScripts.mv_x_pattern.sql" -> "mv_x_pattern"
    private static string ViewNameOf(string resourceName)
    {
        var withoutExtension = resourceName[..^".sql".Length];
        return withoutExtension[(withoutExtension.LastIndexOf('.') + 1)..];
    }
}
