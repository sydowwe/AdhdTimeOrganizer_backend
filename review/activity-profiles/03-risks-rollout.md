# AdhdTimeOrganizer.ActivityProfiles — 03 Risks & rollout

Point-in-time snapshot, 2026-09-01. Synthesis of [02-findings.md](02-findings.md) and
[01-testing.md](01-testing.md); IDs link back, nothing is restated.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 1 | 🔴 | `GET /api/activity-backlog-profile` returns every user's backlog profiles — no user scoping anywhere on the path, and no test | SEC-1 | `application/endpoint/backlog/query/GetAllActivityBacklogProfileEndpoint.cs:8` |
| 2 | 🟠 | Profile create **and** update accept another user's lookup ids; the victim can then no longer delete their own lookup row (`Restrict`) | SEC-2 | `application/validator/CreateActivityBacklogProfileValidator.cs:15-44`, `UpdateActivityBacklogProfileValidator.cs:9-14` |
| 3 | 🟠 ✅ fixed 2026-09-04 | `PUT /memory-anchor/{id}` repoints the anchor onto any activity in the database, past both create-time rules | SEC-3 | `application/dto/request/MemoryAnchorRequest.cs:18` |
| 4 | 🟠 | A forecast with an empty `daily` block is read as a calm dry day and paints a "dry" badge — a wrong opinion where the contract promises none | CQ-5 | `infrastructure/extService/weather/OpenMeteoWeatherProvider.cs:132` |
| 5 | 🟠 ✅ fixed 2026-09-04 | Three dev seeders gate on a global row count, so only the first user is ever seeded | CQ-3 | `infrastructure/persistence/seeder/dev/ActivityBacklogProfileSeeder.cs:26` (+2) |
| 6 | 🟠 | Concurrency conflicts on every custom profile write are flattened to 400 instead of 409 | CQ-2 | `application/endpoint/backlog/command/UpdateActivityBacklogProfileEndpoint.cs:50-55` (+11) |
| 7 | 🟠 | 18 hand-written profile endpoints re-implement bases that already have the `AuthorizeAsync` hook — the root cause of #1, #6 and #9 | CQ-1 | `application/endpoint/{backlog,bucketList,project}/**` |
| 8 | 🟠 ✅ fixed 2026-09-04 | Dev seeders re-declare the lookup defaults and can create weather rows with no `Code` | CQ-4 | `.../dev/ActivityBacklogProfileSeeder.cs:39-44,92-113` |
| 9 | 🟠 | Batch delete is one query per id, each `Include`-ing the whole activity | PERF-2 | `application/endpoint/backlog/command/BatchDeleteActivityBacklogProfileEndpoint.cs:31-45` (+2) |
| 10 | 🟠 | A draw costs up to nine sequential round trips | PERF-1 | `application/endpoint/leisureSuggestion/DrawLeisureSuggestionEndpoint.cs:61-107` |
| 11 | 🟠 ✅ fixed 2026-09-04 | `testing.md` denies the unique index the merge seam's collision rule is built on | DOC-1 | `docs/testing.md:66` |
| 12 | 🟠 | Three docs describe the backlog `GetAll` and none mentions it is unscoped | DOC-2 | `docs/summary.md:60-69`, `docs/domain-map.md:97`, `docs/testing.md:109-111` |
| 13 | 🟡 ✅ fixed 2026-09-04 | `ComfortZoneStep` validated 1–10, scored as 1–5 (negative comfort score from step 7) — **`MIG-4`'s data pass is still owed** | CQ-13 / DOC-3 | `application/validator/CreateActivityBucketListProfileValidator.cs:15` |
| 14 | 🟡 | Existing-record read uses the unowned id list (safe only because of the `UserId` predicate beside it) | CQ-6 | `application/endpoint/leisureSuggestion/RecordSeenLeisureSuggestionEndpoint.cs:80-82` |
| 15 | 🟡 | Every `DbUpdateException` is treated as the unique-index race; a real failure becomes a misleading 500 | CQ-7 | `.../RecordSeenLeisureSuggestionEndpoint.cs:126` |
| 16 | 🟡 | Shared static response instance on the no-signal paths | CQ-8 | `application/endpoint/leisureSuggestion/GetLeisureWeatherFitEndpoint.cs:38` |
| 17 | 🟡 | `Infer` matches four-character substrings in free user text | CQ-10 | `domain/model/WeatherDependencyCodes.cs:47-54` |
| 18 | 🟡 ✅ fixed 2026-09-04 | Dev seeder pinned to `2026-05-19` | CQ-11 | `.../dev/MemoryAnchorSeeder.cs:57` |
| 19 | 🟡 ✅ fixed 2026-09-04 | Seeder registry key still says `ActivityDiyProfile` | CQ-12 | `.../dev/ActivityProjectProfileSeeder.cs:15` |
| 20 | 🟡 ✅ fixed 2026-09-04 | Dead clamp in the staleness signal | CQ-9 | `domain/service/LeisureDrawRanker.cs:230` |
| 21 | 🟡 ✅ fixed 2026-09-04 | Endpoint count: the smoke test says 53, the tree and both docs say 57 | DOC-4 | `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityProfilesRouteSmokeTests.cs:25,27` |

Test gaps `TEST-1`…`TEST-9` are ranked in [01-testing.md](01-testing.md) §12; `TEST-1`, `TEST-2` and
`TEST-4` are the ones that pin the top three security findings and should land with the fixes.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **The structural fix for `SEC-1` — a mirrored user-scope query filter on the three profiles — silently empties every read that runs with no ambient user.** The dev seeders, `ActivityProfileActivityReferenceSource` (merge/repoint) and the export/erasure paths all read profiles outside a request | High if the `ScopeUserId == null \|\|` escape is omitted | Copy `AppDbContext.cs:321-323` (`RoutinePeriodCompletion`) **exactly**, including the `ScopeUserId == null \|\|` prefix and the `scopingEnabled` gate. It must read `ScopeUserId` off the context, never a captured service — see `persistence.md` → user-scoping filters | MIG-1 |
| **The filter must be named, or it replaces the archived filter that is already on those three entities.** `AppDbContext.cs:299-306` puts `AppQueryFilters.ActivityArchived` on all three; an unnamed `HasQueryFilter` would replace it, un-hiding archived profiles with no error — the exact failure the comment at `:292-295` records having already happened once on `Activity` | Medium | Add a new constant to `AppQueryFilters` (e.g. `ActivityProfileUserScope`) and use the two-argument overload. `NamedQueryFilterTests` is the guard; extend it to the three new names | MIG-2 |
| **`SEC-2`'s fix rejects writes that succeed today, and existing rows may already be invalid.** Any profile already pointing at a foreign lookup will fail its next update with a validation error the user cannot act on | Low (needs a hand-crafted request to have happened) but unbounded in a shared DB | Before deploying, run the audit query: profiles joined to their lookups where `lookup.user_id <> activity.user_id`. Repoint or null them out; there is no schema change, so this is a data check rather than a migration | MIG-3 |
| ⚠ **live 2026-09-04** · **Tightening `ComfortZoneStep` to 1–5 (`CQ-13`) invalidates existing rows in 6–10.** Their next update is rejected until the value is changed | Low | The validator change is **in**; the data pass is **not**. Before deploying, run `SELECT id, comfort_zone_step FROM activity_profiles.activity_bucket_list_profile WHERE comfort_zone_step > 5` and clamp any hits to 5 — a plain `UPDATE`, no migration. No DB check constraint was added, deliberately, so nothing rejects a stored out-of-range row on read | MIG-4 |
| **The four pinned FK constraint names are a lock-taking DDL away from a rewrite.** Removing a `HasConstraintName` or re-adding one of the deleted `Activity`/`User` navigations emits DROP + ADD CONSTRAINT per table — `ACCESS EXCLUSIVE` plus a full revalidation, for no schema benefit | Low, but silent | The names are documented in `summary.md:52-58` and each configuration's comment. `ActivityForeignKeyInventoryTests` is the guard. Always inspect `migrations add` output before applying | MIG-5 |
| **"At most 2 anchors per activity per month" cannot be moved into the schema.** A `≤ 2` rule is not a unique index; it needs a trigger or an exclusion constraint. `SEC-3` closes the update hole in application code only | — | Keep it in the validator, and make `TEST-4` the pin. Do not attempt a DB constraint for this in the same change | MIG-6 |
| ✅ done 2026-09-04 · **`CQ-1`'s migration to the framework bases changes status codes on 18 routes** (403 vs 404 on unauthorised, and the base `Update` returns 200 with the id where the custom one returns 204) | High if done without care | Landed with `NotFoundWhenUnauthorized` on the three `GetById`s, so the 404s stand; the batch deletes would answer 403 only in a host without the profile filter. **The 204→200 on the three `PUT`s is live and the client repo has not been checked** — that is the one open follow-up | MIG-7 |
| **Drift risk: none within the repo** — there is no sandbox or duplicate copy of this module, and it is referenced only by the host and the integration tests (`summary.md:38-39`). The live drift surface is `framework/`, a **submodule**: any change to `BaseGetAllEndpoint` (e.g. adding the missing `ApplyUserScoping` hook) is a two-repo operation and is invisible in the parent's `git status` beyond "modified: framework (new commits)" | Medium if a base class is touched | Commit and push in `framework/` first, then commit the new gitlink here. Prefer fixing `SEC-1` in-slice **and** in `AppDbContext` rather than reaching into the submodule for a one-endpoint problem | MIG-8 |

## Audit-log gaps

Auditing exists in `Sydowwe.Framework` but is **not wired up in this host** (`CLAUDE.md`), so nothing
below is losing a row today. They are the two places that will be wrong on the day it is switched on,
and both are cheap to get right now.

| Gap | Detail | ID |
|---|---|---|
| **The retention sweep bypasses the ChangeTracker** | `RecordSeenLeisureSuggestionEndpoint.PruneAsync:154-160` is an `ExecuteDeleteAsync` — the one bulk operation in the slice. When the audit interceptor is wired it will see nothing, and this is precisely the operation an Art. 5(1)(e) retention story wants evidence of ("rows older than 90 days were deleted, on this date, for this user"). Either log the deleted count through `IAuditService.LogAsync`, or accept the bypass **in a comment on the method** so the next reader does not assume it is covered | AUDIT-1 |
| **Two free-text personal fields carry no audit attribute** | `MemoryAnchor.HighlightNote` (1000 chars of "what was memorable about this month") and `ActivityBucketListProfile.InspirationSource` (500 chars) are user-authored reflections. With auditing on, every edit copies them in full into the audit trail — which then survives an erasure that cascades the source rows, and which `PiiRedactor` cannot scrub because free-text PII is not regex-matchable (`CLAUDE.md` → Logging). Decide now: `[AuditIgnore]` on both properties, or `[NoAudit]` on `MemoryAnchor` as a whole | AUDIT-2 |
| Dev seeder truncation | The eight dev seeders call `TruncateTableCascadeAsync<T>()`, which is raw SQL and bypasses everything. Dev-only and by design — noted, not flagged | — |

Also worth carrying into whichever pass wires auditing: the six user-scoped entities in this slice are
on `UserDataExportAndDeletionTests.KnownGaps` (see `docs/testing.md` → Known gaps). Erasure is
unaffected — everything here cascades from `User` or `Activity` — but an audit trail that outlives a
cascade is a new copy of data the export does not know about.
