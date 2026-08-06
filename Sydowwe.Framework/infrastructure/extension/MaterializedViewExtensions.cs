using Microsoft.EntityFrameworkCore;

namespace Sydowwe.Framework.infrastructure.extension;

public static class MaterializedViewExtensions
{
    public static async Task RefreshMaterializedViewAsync(this DbContext context, string viewName, CancellationToken ct = default)
    {
        // viewName is a SQL identifier and cannot be parameterized, so EF1002 is unavoidable.
        // Double any embedded quotes to neutralize injection in case a caller passes untrusted input.
        var safeName = viewName.Replace("\"", "\"\"");
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync($"""
                                                   REFRESH MATERIALIZED VIEW CONCURRENTLY "public"."{safeName}"
                                                   """, ct);
#pragma warning restore EF1002
    }
}