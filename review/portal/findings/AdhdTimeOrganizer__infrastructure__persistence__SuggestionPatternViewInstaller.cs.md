# Review: AdhdTimeOrganizer/infrastructure/persistence/SuggestionPatternViewInstaller.cs
Role: other
Summary: Small, well-documented startup helper that idempotently creates missing materialized views from embedded SQL; sound for a single-instance startup but has no protection against concurrent creators.
Coverage: n/a

## Issues
- [Medium][Concurrency] SuggestionPatternViewInstaller.cs:38-50 — the "check `to_regclass`, then `CREATE MATERIALIZED VIEW`" sequence is not atomic and the embedded scripts (unverified, but implied by the pattern) likely use plain `CREATE MATERIALIZED VIEW` rather than `IF NOT EXISTS`; two instances/workers starting concurrently (or a container restarting into a rolling deployment) can both observe the view missing and both attempt to create it, and the loser crashes at startup with a Postgres "relation already exists" error.
  Why: This runs on every app boot (per Program.cs call site) with no advisory lock, so any multi-instance or fast-restart deployment is exposed to a startup crash that single-instance dev/test never exercises.
  Fix: Either wrap the check+create in a `pg_try_advisory_lock`/`pg_advisory_lock` pair, or make the scripts themselves idempotent (`CREATE MATERIALIZED VIEW IF NOT EXISTS` doesn't exist in Postgres for matviews pre-15 verbatim the same way — but you can `CREATE ... IF NOT EXISTS` is actually supported for materialized views since PG 9.3, so use it) and treat a duplicate-object error as benign.
  Confidence: Med

- [Low][Quality] SuggestionPatternViewInstaller.cs:30 — resource matching uses `name.Contains(ResourceFolder, ...)` rather than requiring the folder segment to be a genuine path prefix (e.g. anchored at a namespace boundary); a future embedded resource elsewhere in the assembly whose full name happens to contain the literal substring `.infrastructure.persistence.sqlScripts.` would be silently picked up and executed as DDL.
  Why: Low real-world likelihood today (only three resources exist, all correctly located), but the loose match makes the resource selection a bit surprising to reason about compared to `StartsWith`.
  Fix: Anchor with `StartsWith($"{assembly.GetName().Name}{ResourceFolder}")` or similar instead of `Contains`.
  Confidence: Low

- [Low][Quality] SuggestionPatternViewInstaller.cs:31 — view creation order is derived from `.Order()` on the raw embedded-resource name string (effectively alphabetical), not from any explicit dependency declaration; if a future materialized view needs to reference an earlier one, alphabetical ordering is not guaranteed to satisfy that dependency.
  Why: Silent breakage on a future addition — the failure would surface as a Postgres "relation does not exist" only when a new script violates the implicit alphabetical assumption.
  Fix: Either keep views mutually independent by convention (document it, as the class remarks partly do) or make dependency order explicit (e.g. a leading numeric prefix in the filename).
  Confidence: Low

- [Nit][Quality] SuggestionPatternViewInstaller.cs:26 — no try/catch around `ExecuteSqlRawAsync`; any failure (bad SQL, missing dependent table) takes down the whole startup path unconditionally.
  Why: This is likely intentional per the class remarks (fail loudly instead of surfacing later as a 500 during a save), so flagged only as a note, not a defect.
  Fix: None needed unless a softer degrade (log + continue) is desired for optional views.
  Confidence: Low
