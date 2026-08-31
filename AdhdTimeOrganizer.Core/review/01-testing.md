# AdhdTimeOrganizer.Core — 01 · Testing

Point-in-time snapshot, 2026-08-31. Companion files: [02-findings.md](02-findings.md),
[03-risks-rollout.md](03-risks-rollout.md). The durable map is `../docs/domain-map.md`; this file does
not restate it.

## Test infrastructure summary

Core has **no test project of its own** — everything lives in `AdhdTimeOrganizer.IntegrationTests`
(121 files), which is correct for this project: almost every rule Core owns is a property of the
*composed* app (the `DbContext` → `AppDbContext` alias, the global query filters, the two DI scans,
the seam registrations), and none of it is observable from a library-only unit test. The
infrastructure is `Sydowwe.Framework.Testing`: `PostgresTestBase` over the `"Postgres"` collection and
`AppDbContextFixture` (a real Postgres container), with `UserRoleTestAuthHandler` behind
`CreateUserRoleClient()` / `CreateAdminRoleClient()` / `CreateFactory(roles, userId)` for role and
ownership scenarios. CRUD shapes come from the Framework base test classes
(`BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
`BaseGetByIdEndpointTests`, `BaseGetSelectOptionsEndpointTests`, `BaseBatchDeleteEndpointTests`,
`BaseGridEndpointTests`, `BaseFetchTableEndpointTests`); everything Core actually *decides* is
hand-written.

Two pieces are bespoke and worth knowing about before you add tests:

- **`ActivityEndpointTests.cs` is a 1100-line multi-slice file.** It holds the Core activity/role/
  category base-test subclasses *and* the ActivityProfiles lookups and `MemoryAnchor`, plus a
  `file static class Seed` helper at the top. It is the file a Core CRUD test goes in, and it is
  already at the size where finding the right class is the hard part.
- **`UnauthorizedStatus => HttpStatusCode.NotFound` is overridden on nine of those subclasses**
  (`ActivityEndpointTests.cs:121,157,178,205,237,254,281,313,330`). That is deliberate — the per-user
  global filter turns a foreign row into a 404 before any authorization hook runs — but it means the
  base "unauthorized" assertion in those classes is asserting *scoping*, not *authorization*. Nothing
  in Core asserts a plain **401** on any route.
- **`DefaultUsersSeederTests`** ships its own in-memory `ILogger` capture (`:378-420`) so it can
  assert that a failed root-admin seed logs and never prints the raw email. That is the only
  log-assertion harness in the suite; reuse it rather than writing a second one.

Fragility notes: every Core test shares one Postgres container across the collection, so tests seed
per-test users with `Guid`-suffixed emails (`Seed.SecondUserAsync`) rather than truncating. Two dev
seeders (`ActivityRoleSeeder`, `ActivityCategorySeeder`, `ActivitySeeder`) call
`TruncateTableCascadeAsync` on the **whole table**, so they must never run inside the shared
collection — they don't today, but nothing stops it.

## 7. Test quality assessment

**Strengths — this is above the bar for the codebase.**

- *Assertions are on rows, not routes, where it counts.*
  `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` (`:90`) checks surviving rows in six
  projects rather than the `repointedCount` the endpoint returns — which is the one assertion a
  dropped seam registration cannot fake. `ActivityArchivingTests.UsageCount_IsSortable` (`:409`)
  catches `usageCount` moving out of the SQL projection, a failure that otherwise returns the wrong
  page in the right-looking order with no error.
- *The model-level guards are real.* `ActivityForeignKeyInventoryTests` (`Modules/`) freezes all 19
  activity FKs, their requiredness, uniqueness and delete behaviour as a hand-written literal and
  compares whole. Its own docstring records that "every activity FK is Cascade" was asserted in
  `MergeActivityEndpoint`'s remarks *while quietly false*. That is the right lesson learned in the
  right place.
- *`SeamWiringTests` (6 tests) pins the thing that cannot fail loudly* — both keyed source families
  cover every key **exactly** once, the three single seams resolve exactly once, and every
  `ISeamEvent` has a handler.
- *Negative paths are covered where the ask was explicit.* `ActivityRoleSystemKeyTests` covers rename
  survival, an unknown key **not** falling through to `SystemKey == null`, the 409 on deleting a keyed
  role, and re-seed idempotence on a renamed role — four separate silent failures, each with its own
  test.
- *`Seed_DuplicateEmailFailure_NeverLogsTheRawEmailAddress`* is a PII regression pin, and it is the
  only one in the solution. Keep it.

**Weaknesses.**

- **The Framework base-test subclasses are the weakest tests in the file and they are the majority.**
  A class like `ActivityCategoryDeleteTests` (`:326`) supplies a seeded id and inherits its
  assertions; it proves the route exists and returns 204, and it proves nothing about what the delete
  *took with it*. For `ActivityCategory` and `ActivityRole` that is exactly where the risk is —
  see `SEC-1`. This is base-class coverage being read as behavioural coverage.
- **No 401 anywhere.** Every "unauthorized" assertion in the Core area is a cross-user 404 (which is
  well covered — `Archive_OtherUsersActivity_Returns404`, `Merge_AcrossUsers_Returns404_AndTouchesNothing`,
  `Clone_ForeignActivityId_Returns404`, `QuickEdit_ForeignActivityId_Returns404`). The four
  hand-rolled endpoints (`merge`, `archived`, `clone`, `quick-edit`) get their roles from
  `Program.cs:573`'s global configurator, not from their own `Configure()` — three of them never call
  `Roles()` at all — and nothing asserts that the configurator is still doing it.
- **Writes are never checked for FK ownership.** The suite proves you cannot *read* another user's
  row; it never checks you cannot *point at one*. `TimerPresetCrudAuthMatrixTests` is the clearest
  case: it seeds foreign presets for six read/update/delete scenarios and never once posts a foreign
  `activityId`. Same gap the TodoLists review already logged as its `SEC-1`.
- **`TimerPresetCrudAuthMatrixTests` contains zero `[Fact]`s of its own** — it is nine base-test
  subclasses. That is fine, but the filename promises an auth *matrix* and there isn't one; a reader
  looking for "who may call this" finds a file that looks like it answers the question.
- **Assertion theatre is rare but present**: `TimerPresetValidationTests` is three tests, all
  `Returns400`, none asserting *which* field failed or that no row was written.

## 8. Coverage matrix

### Endpoints (39 routes + 1 shared base)

Legend: ✅ covered · ⚠️ base-class only (route + status, no behaviour) · ❌ none.
"Auth" = cross-user/role behaviour asserted for that route specifically.

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `GET /activity/filtered-table` (Grid) | ✅ | ✅ tri-state archive, sortable usageCount | ⚠️ | `ActivityArchivingTests:175,189,207,353,409` |
| `GET /activity/all` (GetAll) | ❌ | ❌ | ❌ | — · `TEST-11` |
| `GET /activity/{id}` | ✅ | ✅ archived row, usageCount/canDelete | ✅ 404 | `ActivityArchivingTests:284,386`; `ActivityEndpointTests:117` |
| `GET /activity/all-options` | ✅ | ✅ excludes archived | ⚠️ | `ActivityArchivingTests:224`; `ActivityEndpointTests:189` |
| `GET /activity/form-select-options` | ✅ | ✅ excludes archived | ❌ | `ActivityArchivingTests:236` |
| `POST /activity` | ✅ | ✅ dup name 409, no role 400 | ⚠️ | `ActivityEndpointTests:140,802,887` |
| `PUT /activity/{id}` | ✅ | ✅ does not un-archive | ⚠️ | `ActivityEndpointTests:153`; `ActivityArchivingTests:151` |
| `DELETE /activity/{id}` | ✅ | ✅ cascades to planner tasks | ⚠️ | `ActivityEndpointTests:174,903` |
| `PUT /activity/{id}/quick-edit` | ✅ | ⚠️ **archived row untested** | ✅ 404 | `ActivityEndpointTests:1036,1061` · `TEST-7` |
| `POST /activity/{id}/clone` | ✅ | ✅ copy suffix; ⚠️ **timestamps, archived row untested** | ✅ 404 | `ActivityEndpointTests:946,1003,1021` · `TEST-7`, `TEST-8` |
| `PATCH /activity/{id}/archived` | ✅ | ✅ idempotent, unknown id | ✅ 404 | `ActivityArchivingTests:76,101,112,124` |
| `POST /activity/merge` | ✅ | ✅ 8 edges; ❌ **archived merged id**, ❌ **archived survivor w/ profile** | ✅ 404 | `ActivityMergeTests` (13) · `TEST-3`, `TEST-4` |
| `POST /activity-role` | ✅ | ✅ dup name 409 | ⚠️ | `ActivityEndpointTests:224,833` |
| `PUT /activity-role/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:233` |
| `DELETE /activity-role/{id}` | ✅ | ✅ 409 on system key; ❌ **cascade blast radius** | ⚠️ | `ActivityRoleSystemKeyTests:166,186` · `TEST-1` |
| `GET /activity-role/all` | ❌ | ❌ | ❌ | — · `TEST-11` |
| `GET /activity-role/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:201` |
| `GET /activity-role/by-Name/{v}` | ❌ | ❌ | ❌ | — · `TEST-12` |
| `GET /activity-role/by-system-key/{v}` | ✅ | ✅ rename, unknown key, no-match, cross-user | ✅ | `ActivityRoleSystemKeyTests` (9) |
| `GET /activity-role/all-options` | ✅ | ✅ unaffected by archiving | ⚠️ | `ActivityArchivingTests:300`; `ActivityEndpointTests:265` |
| `GET /activity-role/filtered-table` | ⚠️ | ❌ filter predicates untested | ⚠️ | `ActivityEndpointTests:216` |
| `POST /activity-category` | ✅ | ✅ dup name 409 | ⚠️ | `ActivityEndpointTests:300,859` |
| `PUT /activity-category/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:309` |
| `DELETE /activity-category/{id}` | ⚠️ | ❌ **cascade blast radius** | ⚠️ | `ActivityEndpointTests:326` · `TEST-1` |
| `POST /activity-category/batch-delete` | ✅ | ✅ foreign id 404 | ✅ | `ActivityEndpointTests:357,371` · but `TEST-1` |
| `GET /activity-category/all` | ❌ | ❌ | ❌ | — · `TEST-11` |
| `GET /activity-category/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:277` |
| `GET /activity-category/all-options` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:341` |
| `GET /activity-category/filtered-table` | ✅ | ✅ role join + RoleSortKey redirect | ⚠️ | `BaseFetchTableEndpointTests:69,85,138,154,173` |
| `POST /timer-preset` | ✅ | ✅ duration ≤ 0 400; ❌ **foreign activityId** | ⚠️ | `TimerPresetCrudTests:106`; `TimerPresetValidationTests:16` · `TEST-2` |
| `PUT /timer-preset/{id}` | ✅ | ✅ full overwrite | ✅ | `TimerPresetCrudTests:130`; `…AuthMatrix:121` |
| `DELETE /timer-preset/{id}` | ⚠️ | ✅ cascade from activity | ✅ | `TimerPresetCrudTests:147`; `…AuthMatrix:136` |
| `GET /timer-preset/all` | ✅ | ✅ caller's own only | ✅ | `TimerPresetCrudTests:163`; `…AuthMatrix:93` |
| `GET /timer-preset/{id}` | ✅ | ✅ | ✅ | `TimerPresetCrudTests:106`; `…AuthMatrix:87` |
| `POST /pomodoro-timer-preset` | ✅ | ✅ focus ≤ 0, empty name; ❌ **foreign activityIds** | ⚠️ | `TimerPresetValidationTests:26,42` · `TEST-2` |
| `PUT /pomodoro-timer-preset/{id}` | ✅ | ✅ full overwrite | ✅ | `TimerPresetCrudTests:232`; `…AuthMatrix:205` |
| `DELETE /pomodoro-timer-preset/{id}` | ⚠️ | ✅ cascade from focus activity | ✅ | `TimerPresetCrudTests:264`; `…AuthMatrix:220` |
| `GET /pomodoro-timer-preset/all` | ✅ | ✅ caller's own only | ✅ | `TimerPresetCrudTests:281`; `…AuthMatrix:161` |
| `GET /pomodoro-timer-preset/{id}` | ✅ | ✅ | ✅ | `TimerPresetCrudTests:191`; `…AuthMatrix:155` |
| `BaseActivityFormSelectOptionsEndpoint<T>` | ✅ | ✅ `?includeArchived=` both ways | ❌ | `ActivityArchivingTests:255` |

Cross-cutting, not per-route: `CoreRouteSmokeTests` (2) pins Core's assembly in FastEndpoints'
`o.Assemblies` and the exactly-once DI scan; `ExtensionToken_CannotAccess_ActivityEndpoint_Returns403`
(`ActivityEndpointTests:1091`) is the only extension-client assertion in the area.

### Services, seams, seeders, configuration

| Unit | Tests | Notes |
|---|---|---|
| `ActivityReferenceService` | ~6, indirect | Exercised through `ActivityArchivingTests:353,386,409` and all 13 `ActivityMergeTests`. No direct test; `CountByActivityAsync` on an **archived** activity is the hole (`TEST-4`). |
| `TimerPresetActivityReferenceSource` | 2, indirect | `Merge_RepointsEveryReferencingSlice`, `Merge_ReportsWhatItDid`. The two-columns-on-one-row double count is covered by `Merge_OneRowReferencingTwoMergedActivities_CollapsesToOneReference:196`. |
| `UserDefaultsService` | 3, indirect | Through `DefaultUsersSeederTests:233,152,123`. No test of the failure path returning `Result.Failed`. |
| `UserTimeZoneResolver` | 0 direct | Behaviour asserted in `HistoryDashboardUserZoneTests` / `TrackingDashboardUserZoneTests` / `QuietHoursUserZoneAndLateDeliveryTests`. **The documented "missing user ⇒ UTC, not `TimeZoneInfo.Local`" fallback is asserted nowhere** (`TEST-13`). |
| `SeedUserIdProvider` | 1 | `DefaultUsersSeederTests:290` (null when `ROOT_ADMIN_EMAIL` unset). |
| `DefaultUsersSeeder` | 12 | Best-covered unit in Core. Create/override/idempotence/role/PII all pinned. |
| `DefaultActivityRoleSeeder` · `TimerPresetSeeder` · `PomodoroTimerPresetSeeder` | 14 (theory-driven) + 9 matcher | `PerUserDefaultSeederTests` + `PerUserDefaultMatcherTests`. Idempotence, gap-refill, reset-in-place, rename survival — thorough. |
| Dev seeders (`ActivityRole`/`Category`/`Activity`) | 0 | Never executed by any test. Their whole-table truncate is unasserted (`TEST-14`). |
| Seam surface | 6 | `SeamWiringTests`. |
| EF configuration | 3 | `ActivityForeignKeyInventoryTests` (FKs *into* Activity), `ModuleSchemaTests`, `NamedQueryFilterTests`. **No inventory of FKs *out of* Activity** — which is why `SEC-1` is invisible. |
| Validators | 5, indirect | Only `TimerPresetValidationTests` + the two 400 paths in `ActivityEndpointTests`. The Icon-length mismatch (`CQ-1`) and the never-invoked `TimeDtoValidator` (`CQ-2`) are both uncovered. |
| `PortalRoleCatalog` · `PortalAuthorizationPolicies` | indirect | Through every role-gated test; `Root` assignment pinned at `DefaultUsersSeederTests:233`. |

### The honest number

**≈ 72% ship-worthy coverage.** By route: 39 endpoints, 21 with genuine behavioural assertions, 14
base-class-only, 4 with none. That number is *high quality where it exists* — the merge, archiving,
system-key and seeding suites are the best in the solution — and the residue is concentrated, not
spread thin.

**The riskiest gaps, in order:**

1. **Lookup deletion.** `DELETE /activity-category/{id}`, its batch sibling and
   `DELETE /activity-role/{id}` are covered only by base-class tests that delete an *unused* row.
   Nothing asserts what happens when the row is in use, and the answer (`SEC-1`) is that every
   activity under it is destroyed, and through the activity cascade every history row, planner task,
   to-do item and profile that pointed at those activities. This is the single largest gap in Core and
   it is invisible because a green delete test sits next to it.
2. **Write-side FK ownership** (`SEC-2`). Five request DTOs copy caller-supplied FK ids straight into
   columns with no ownership check, and the suite's entire notion of "auth" is read-side.
3. **The archival filter inside the reference seam** (`SEC-4`) — `usageCount` on an archived activity,
   and a merge into an archived survivor that already holds a profile.

## 12. Missing tests — backlog

Prompt files live in [`testingPrompts/`](testingPrompts/) and are paste-ready for a context-less
agent. Read [`testingPrompts/_SHARED-CONTEXT.md`](testingPrompts/_SHARED-CONTEXT.md) first — it holds
the fixture, auth-client and `KnownGap` conventions the individual prompts assume.

| ID | Gap | Finding | Prompt |
|---|---|---|---|
| `TEST-1` | Deleting a role or category destroys every activity under it, and their cross-slice rows | `SEC-1` | [ActivityLookupDeleteCascade.md](testingPrompts/ActivityLookupDeleteCascade.md) |
| `TEST-2` | Core writes accept another user's `roleId` / `categoryId` / `activityId` | `SEC-2` | [CoreForeignKeyOwnership.md](testingPrompts/CoreForeignKeyOwnership.md) |
| `TEST-3` | `POST /activity/merge` 404s when a merged id is archived | `SEC-3` | [MergeArchivedMergedId.md](testingPrompts/MergeArchivedMergedId.md) |
| `TEST-4` | `usageCount` on an archived activity omits filtered dependents; merge into an archived survivor holding a profile | `SEC-4` | [ArchivedActivityReferenceCount.md](testingPrompts/ArchivedActivityReferenceCount.md) |
| `TEST-5` | An `icon` of 51–255 chars passes validation and fails in Postgres | `CQ-1` | [IconLengthValidatorVsColumn.md](testingPrompts/IconLengthValidatorVsColumn.md) |
| `TEST-6` | `TimeDto` hours/minutes unvalidated on the requests Planning does not hand-check | `CQ-2` | [TimeDtoRangeValidation.md](testingPrompts/TimeDtoRangeValidation.md) |
| `TEST-7` | Quick-edit and clone 404 on an archived activity while update and get-by-id do not | `CQ-3` | [QuickEditCloneArchived.md](testingPrompts/QuickEditCloneArchived.md) |
| `TEST-8` | `Activity.Clone()` copies the source's created/modified timestamps | `CQ-4` | [CloneTimestamps.md](testingPrompts/CloneTimestamps.md) |
| `TEST-9` | Grid `Contains` filters are case-sensitive against a Postgres `LIKE` | `PERF-2` | [GridFilterCaseSensitivity.md](testingPrompts/GridFilterCaseSensitivity.md) |
| `TEST-10` | No 401 assertion on any Core route; the four hand-rolled endpoints rely on the global configurator | `CQ-3` | [CoreEndpointAuthMatrix.md](testingPrompts/CoreEndpointAuthMatrix.md) |
| `TEST-11` | `GET /activity/all`, `/activity-role/all`, `/activity-category/all` have no test at all | — | [GetAllEndpointCoverage.md](testingPrompts/GetAllEndpointCoverage.md) |
| `TEST-12` | `GET /activity-role/by-Name/{v}` is routed and untested | `CQ-7` | [GetAllEndpointCoverage.md](testingPrompts/GetAllEndpointCoverage.md) |
| `TEST-13` | The "missing user ⇒ UTC, never `TimeZoneInfo.Local`" fallback is asserted nowhere | `CQ-10` | [UserTimeZoneResolverFallback.md](testingPrompts/UserTimeZoneResolverFallback.md) |
| `TEST-14` | Dev seeders truncate the whole table for every user; nothing pins that | `CQ-11` | [DevSeederTruncateScope.md](testingPrompts/DevSeederTruncateScope.md) |
