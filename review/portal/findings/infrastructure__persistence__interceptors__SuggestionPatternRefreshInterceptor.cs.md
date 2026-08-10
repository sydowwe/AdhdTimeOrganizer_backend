# Review: AdhdTimeOrganizer/infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs
Role: other (SaveChanges interceptor)
Summary: Synchronously runs up to three `REFRESH MATERIALIZED VIEW CONCURRENTLY` statements inline on every save touching PlannerTask/ActivityHistory/Calendar, with no failure isolation and no debouncing — a real latency and reliability risk once the views are actually installed (see CLAUDE.md: currently only wired via `SuggestionPatternRefreshInterceptor`, not through the audit interceptor).

## Issues
- [High][Performance] SuggestionPatternRefreshInterceptor.cs:35-45 — Every save that touches even a single `PlannerTask`/`ActivityHistory`/`Calendar` row synchronously awaits a full `REFRESH MATERIALIZED VIEW CONCURRENTLY` before the request completes, regardless of how small the change was.
  Why: `REFRESH CONCURRENTLY` rebuilds the view from a full scan of its source query and additionally diffs old vs. new rows to do the concurrent swap — it is O(view size), not O(change size), so a one-row `PlannerTask` edit pays for a full history-pattern rebuild on the request thread, directly inflating p99 latency for any create/update/delete on these three hot entities.
  Fix: Move the refresh off the request path — debounce/coalesce per view (e.g. mark "dirty" and let a scheduled job in `Sydowwe.Scheduler` refresh on an interval) instead of refreshing synchronously on every SaveChanges.
  Confidence: High

- [High][Concurrency] SuggestionPatternRefreshInterceptor.cs:36-45 — `REFRESH MATERIALIZED VIEW CONCURRENTLY` on the same view takes a lock that blocks a second concurrent `REFRESH CONCURRENTLY` on that same view until the first completes; it does not queue, it serializes.
  Why: Under any real concurrent write load on `PlannerTask` (the common case — multiple users/requests saving tasks around the same time), requests pile up waiting on each other's view refresh, which can cascade into thread-pool/connection-pool exhaustion under load, not just added latency for a single request.
  Fix: Same as above — take the refresh off the synchronous save path so concurrent saves never contend on the view lock.
  Confidence: Med

- [High][Quality] SuggestionPatternRefreshInterceptor.cs:36-45 — If the refresh statement throws (view missing → 42P01, per CLAUDE.md's own note that the views aren't created by `EnsureCreated`; a `REFRESH CONCURRENTLY` failure because the view lacks a unique index; a lock-wait timeout), the exception propagates out of `SavedChangesAsync`, i.e. **after** the entity save has already committed.
  Why: The caller/endpoint sees an unhandled exception / 500 even though the underlying data was persisted successfully — a false-negative failure that misleads the client and any error monitoring, and could trigger unnecessary retries that then hit a duplicate-save code path.
  Fix: Wrap each `ExecuteSqlRawAsync` refresh in its own try/catch, log the failure (view name only, no entity data), and let the save result stand; consider marking the view "stale" for a later retry instead of throwing.
  Confidence: Med

- [Medium][Quality] SuggestionPatternRefreshInterceptor.cs:23-25,47-49 — `_refreshPlanner`/`_refreshHistory`/`_refreshTemplate` are set in `SavingChangesAsync` and only cleared at the end of `SavedChangesAsync`; there is no `SaveChangesFailedAsync` override to reset them.
  Why: If a `SaveChangesAsync` call throws (e.g. concurrency conflict, DB error) after `SavingChangesAsync` ran, the flags stay `true`; on the same scoped `DbContext`/interceptor instance, the *next* successful save — even one touching neither of the three types — will still trigger a stale, unnecessary view refresh.
  Fix: Override `SaveChangesFailedAsync` (or reset the flags at the top of `SavingChangesAsync` unconditionally, which the code already does — but also add a failed-changes reset so a genuinely failed save doesn't leave flags primed for the next unrelated save).
  Confidence: Med

- [Low][Quality] SuggestionPatternRefreshInterceptor.cs:35-45 — The three refresh blocks are copy-pasted with hardcoded view name literals instead of a small `(flag, viewName)` loop/table.
  Why: Easy to typo a view name or forget to add the `if` guard when a fourth pattern view is added later.
  Fix: Collect `(bool flag, string viewName)` tuples and loop, e.g. `foreach (var (flag, view) in checks) if (flag) await db.ExecuteSqlRawAsync($"REFRESH MATERIALIZED VIEW CONCURRENTLY {view}", ct);`.
  Confidence: Low

- [Low][Quality] SuggestionPatternRefreshInterceptor.cs:30-51 — No logging around the refresh (start/duration/failure), despite each call being a potentially multi-second synchronous DB operation embedded in every save.
  Why: Without timing/failure logs, a production latency regression on PlannerTask/ActivityHistory saves would be invisible — nothing here would show up as "the view refresh is slow," it would just look like generic save latency.
  Fix: Log view name + elapsed time (and failures) around each `ExecuteSqlRawAsync` call, no PII involved so this is safe to log verbatim.
  Confidence: Low
