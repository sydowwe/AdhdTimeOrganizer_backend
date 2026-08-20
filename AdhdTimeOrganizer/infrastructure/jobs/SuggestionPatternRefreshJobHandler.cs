using System.Diagnostics;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.infrastructure.jobs;

// PERF-1 / PERF-2 / CQ-9 / CQ-10 (review/portal/02-findings.md) — drains the dirty set that
// SuggestionPatternRefreshInterceptor fills and issues the REFRESH MATERIALIZED VIEW CONCURRENTLY calls that
// used to run synchronously inside the save. The registration's DisallowConcurrent means at most one refresh
// per view is ever in flight, so concurrent saves can no longer contend on the view's own refresh lock, and a
// refresh failure here never surfaces as a 500 on an already-committed save - it's just logged.
//
// A keyed IScheduledJobHandler rather than a Quartz IJob, so the host owns no Quartz either: all scheduling
// goes through Sydowwe.Scheduler. Registered by PortalScheduledJobsRegistrar.
public class SuggestionPatternRefreshJobHandler(
    ISuggestionPatternRefreshQueue queue,
    AppDbContext dbContext,
    ILogger<SuggestionPatternRefreshJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Portal.SuggestionPatternRefresh";

    private static readonly IReadOnlyDictionary<SuggestionPatternView, string> ViewNames = new Dictionary<SuggestionPatternView, string>
    {
        [SuggestionPatternView.PlannerTask] = "mv_planner_task_pattern",
        [SuggestionPatternView.ActivityHistory] = "mv_activity_history_pattern",
        [SuggestionPatternView.TemplateSuggestion] = "mv_template_suggestion_pattern"
    };

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var dirty = queue.DrainDirty();
        if (dirty.Count == 0)
            return;

        var db = dbContext.Database;

        foreach (var view in dirty)
        {
            var viewName = ViewNames[view];
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // viewName always comes from the fixed ViewNames table above, never from external input, so
                // there is no injection risk despite building the statement text ourselves - REFRESH
                // MATERIALIZED VIEW CONCURRENTLY takes a bare identifier, not a bindable parameter.
                //
                // Schema-qualified, and from the model rather than a literal: the views live in the
                // Planning module's schema, and an unqualified name only resolved while every table was
                // in `public`. Worth qualifying carefully because this particular failure is invisible -
                // the catch below logs and swallows, so a wrong name here just means suggestions quietly
                // stop updating.
#pragma warning disable EF1002, EF1003
                await db.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY " + QualifiedViewName(viewName), ct);
#pragma warning restore EF1002, EF1003
                logger.LogInformation(
                    "Refreshed suggestion pattern view {ViewName} in {ElapsedMs}ms", viewName, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Logged only, not rethrown: the data behind this view is already committed, and one view's
                // refresh failure (missing view, lock-wait timeout) must not stop the others from refreshing
                // or fail the job in a way that looks like a data problem.
                logger.LogError(ex,
                    "Failed to refresh suggestion pattern view {ViewName} after {ElapsedMs}ms", viewName, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// The view's schema-qualified name, taken from the entity that maps it with <c>ToView</c>, so
    /// that the schema this refreshes in cannot drift from the one EF reads from.
    /// </summary>
    private string QualifiedViewName(string viewName)
    {
        var schema = dbContext.Model.GetEntityTypes()
                         .FirstOrDefault(e => e.GetViewName() == viewName)
                         ?.GetViewSchema()
                     ?? dbContext.Model.GetDefaultSchema();

        return schema is null ? viewName : $"{schema}.{viewName}";
    }
}
