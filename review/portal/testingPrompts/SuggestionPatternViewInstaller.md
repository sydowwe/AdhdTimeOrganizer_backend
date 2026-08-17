# TEST-15 — SuggestionPatternViewInstaller vs the test fixture's parallel implementation

## Context

`infrastructure/persistence/sqlScripts/*.sql` defines the three suggestion-pattern materialized views.
There are **two independent code paths** that create them:
1. Runtime: `SuggestionPatternViewInstaller` — embedded resources + `to_regclass` existence check.
2. Test fixture: `AppDbContextFixture.OnSchemaCreatedAsync` — reads the same `.sql` files (copied next
   to the test binaries via a `Content` item) and applies them directly, because EF's `EnsureCreated`
   skips them.

Both read the same three files but the two mechanisms can drift silently — this is flagged as `MIG-3`
in the (now-deleted) `03-risks-rollout.md`, but the risk itself still stands regardless of that file's
presence. `Infrastructure/SuggestionPatternRefreshTests.cs` already has a resource/script consistency
check (per the prior review pass) but the fixture still reimplements view creation in parallel rather
than calling the installer directly — that's the actual gap.

## What to write

The fix that actually closes this gap is likely **not** another test but a refactor:
`AppDbContextFixture.OnSchemaCreatedAsync` should call the real `SuggestionPatternViewInstaller`
directly (constructed against the test `DbContext`/connection) instead of hand-applying the `.sql`
files a second time. That eliminates the drift risk structurally rather than pinning it with a test
that itself has to be kept in sync.

If the installer has runtime-only dependencies (hosted-service lifecycle, DI-resolved services) that
make direct reuse awkward in a fixture context, document why in a comment at the call site and instead
add a test that runs the **real installer** against a throwaway schema and diffs its resulting DDL
(`to_regclass`/`pg_matviews` query) against what `OnSchemaCreatedAsync`'s script-based path produces —
that at least catches drift at test time even without eliminating the duplication.

## Out of scope

Don't touch `SuggestionPatternRefreshInterceptor` itself or its existing failure-injection tests in
`SuggestionPatternRefreshTests.cs` — those are already covered (`CQ-9`, `CQ-10`). This is about the
installer/fixture duplication only.
