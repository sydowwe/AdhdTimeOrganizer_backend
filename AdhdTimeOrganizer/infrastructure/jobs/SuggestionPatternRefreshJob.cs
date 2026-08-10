using System.Diagnostics;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdhdTimeOrganizer.infrastructure.jobs;

// PERF-1 / PERF-2 / CQ-9 / CQ-10 (review/portal/02-findings.md) — drains the dirty set that
// SuggestionPatternRefreshInterceptor fills and issues the REFRESH MATERIALIZED VIEW CONCURRENTLY calls that
// used to run synchronously inside the save. DisallowConcurrentExecution means at most one refresh per view
// is ever in flight, so concurrent saves can no longer contend on the view's own refresh lock, and a refresh
// failure here never surfaces as a 500 on an already-committed save - it's just logged.
[DisallowConcurrentExecution]
public class SuggestionPatternRefreshJob(
    ISuggestionPatternRefreshQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SuggestionPatternRefreshJob> logger) : IJob
{
    private static readonly IReadOnlyDictionary<SuggestionPatternView, string> ViewNames = new Dictionary<SuggestionPatternView, string>
    {
        [SuggestionPatternView.PlannerTask] = "mv_planner_task_pattern",
        [SuggestionPatternView.ActivityHistory] = "mv_activity_history_pattern",
        [SuggestionPatternView.TemplateSuggestion] = "mv_template_suggestion_pattern"
    };

    public async Task Execute(IJobExecutionContext context)
    {
        var dirty = queue.DrainDirty();
        if (dirty.Count == 0)
            return;

        var ct = context.CancellationToken;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        foreach (var view in dirty)
        {
            var viewName = ViewNames[view];
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // viewName always comes from the fixed ViewNames table above, never from external input, so
                // there is no injection risk despite building the statement text ourselves - REFRESH
                // MATERIALIZED VIEW CONCURRENTLY takes a bare identifier, not a bindable parameter.
#pragma warning disable EF1002, EF1003
                await db.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY " + viewName, ct);
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
}
