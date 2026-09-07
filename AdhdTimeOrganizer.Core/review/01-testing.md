# AdhdTimeOrganizer.Core — 01 · Testing

Point-in-time snapshot, 2026-08-31; coverage, the quality assessment and the backlog updated
2026-09-08, when the last of the backlog was closed. Companion files: [02-findings.md](02-findings.md),
[03-risks-rollout.md](03-risks-rollout.md). The durable map is `../docs/domain-map.md`; this file does
not restate it.

⚠ **The prompt files in [`testingPrompts/`](testingPrompts/) describe the code as it stood on
2026-08-31 and several of their findings were fixed before their tests were written.** Check the
current source before writing a `KnownGap` pin from one — five of the seven suites added on
2026-09-08 were pinning behaviour the app already had. Two route spellings in the prompts are also
stale: the get-all routes are `GET api/activity` / `api/activity-role` / `api/activity-category`,
not `…/all`.

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
  base "unauthorized" assertion in those classes is asserting *scoping*, not *authorization*. The
  plain **401** lives in `CoreEndpointAuthMatrixTests` instead (since 2026-09-08); read the two
  together before concluding a route's auth is covered.
- **`Seeding/CapturingLoggerProvider.cs`** is the suite's only in-memory `ILogger` capture — register
  it as an `ILoggerProvider` on a factory built by `Fixture.CreateFactory(...)`. `DefaultUsersSeederTests`
  uses it to assert a failed root-admin seed logs without the raw email; `DevSeederTests` to assert
  `ActivitySeeder`'s no-roles warning. Reuse it rather than writing a second one.

Fragility notes: tests seed per-test users with `Guid`-suffixed emails (`Seed.SecondUserAsync`)
because `(user_id, name)` is unique on all three activity tables. The container is shared across the
collection, but `PostgresTestBase.InitializeAsync` Respawns the whole database before **every** test
and xUnit runs a collection sequentially, so no test sees another's rows. That is what makes
`DevSeederTests` (2026-09-08) able to execute the three dev seeders' whole-table
`TruncateTableCascadeAsync` inside the shared collection safely — it is not licence to truncate from
a test that assumes a populated database.

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
- ~~**No 401 anywhere.**~~ ✅ **Closed 2026-09-08.** Every "unauthorized" assertion in the Core area
  was a cross-user 404 (well covered — `Archive_OtherUsersActivity_Returns404`,
  `Merge_AcrossUsers_Returns404_AndTouchesNothing`, `Clone_ForeignActivityId_Returns404`,
  `QuickEdit_ForeignActivityId_Returns404`), which asserts *scoping*, not authorization.
  `CoreEndpointAuthMatrixTests` (29 cases) now pins the 401 on 18 routes, the `DenyExtensionClients`
  403 on the three endpoints that lean hardest on `Program.cs:573`'s global configurator, and that a
  plain `User` still reaches every Core read plus `merge` / `archived`. Note the review's premise has
  since moved: `clone`, `quick-edit` and `BaseActivityFormSelectOptionsEndpoint` all call
  `Roles(AllowedRoles())` today, so only the configurator's extension-denial half is still their sole
  gate.
- ~~**Writes are never checked for FK ownership.**~~ ✅ **Closed.** The suite proved you cannot *read*
  another user's row and never that you cannot *point at one* — `TimerPresetCrudAuthMatrixTests` seeds
  foreign presets for six read/update/delete scenarios and never once posted a foreign `activityId`.
  `CoreForeignKeyOwnershipTests` (7 methods / 8 cases) now covers every Core write that copies a
  caller-supplied id into a column: a foreign `roleId`, `categoryId` (create, quick-edit, clone) and
  `activityId` (both preset families, both pomodoro columns) answer **404**, and each case reads the
  table back with `IgnoreQueryFilters()` — the failure mode was a 201 over a row the caller can never
  see again, so the status code alone proves nothing.
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
| `GET /activity/filtered-table` (Grid) | ✅ | ✅ tri-state archive, sortable usageCount, ✅ case-insensitive name filter + `%`/`_` as literals | ⚠️ | `ActivityArchivingTests:175,189,207,353,409`; `GridTextFilterTests` |
| `GET /activity` (GetAll) | ✅ | ✅ excludes archived | ✅ scoping + 401 | `CoreGetAllEndpointTests` |
| `GET /activity/{id}` | ✅ | ✅ archived row, usageCount/canDelete | ✅ 404 | `ActivityArchivingTests:284,386`; `ActivityEndpointTests:117` |
| `GET /activity/all-options` | ✅ | ✅ excludes archived | ⚠️ | `ActivityArchivingTests:224`; `ActivityEndpointTests:189` |
| `GET /activity/form-select-options` | ✅ | ✅ excludes archived | ❌ | `ActivityArchivingTests:236` |
| `POST /activity` | ✅ | ✅ dup name 409, no role 400 | ✅ 404 on a foreign `roleId` / `categoryId` | `ActivityEndpointTests:140,802,887`; `CoreForeignKeyOwnershipTests` |
| `PUT /activity/{id}` | ✅ | ✅ does not un-archive | ⚠️ | `ActivityEndpointTests:153`; `ActivityArchivingTests:151` |
| `DELETE /activity/{id}` | ✅ | ✅ cascades to planner tasks | ⚠️ | `ActivityEndpointTests:174,903` |
| `PUT /activity/{id}/quick-edit` | ✅ | ✅ reaches an archived row and leaves it archived | ✅ 404, incl. foreign *archived* and foreign `categoryId` | `ActivityEndpointTests:1036,1061`; `ActivityArchivingTests`; `CoreForeignKeyOwnershipTests` |
| `POST /activity/{id}/clone` | ✅ | ✅ copy suffix; ✅ own timestamps, copied vs. overwritten columns; ✅ reaches an archived row, copy starts **active** | ✅ 404, incl. foreign *archived* and foreign `categoryId` | `ActivityEndpointTests:946,1003,1021,1156,1198`; `ActivityArchivingTests`; `CoreForeignKeyOwnershipTests` |
| `PATCH /activity/{id}/archived` | ✅ | ✅ idempotent, unknown id | ✅ 404 | `ActivityArchivingTests:76,101,112,124` |
| `POST /activity/merge` | ✅ | ✅ 12 edges, incl. ✅ **archived merged id** (both directions, mixed sets) and ✅ **archived survivor w/ profile** | ✅ 404, incl. a foreign *archived* id | `ActivityMergeTests` (17) |
| `POST /activity-role` | ✅ | ✅ dup name 409; ✅ icon length 50/51/60 | ⚠️ | `ActivityEndpointTests:224,833`; `NameTextColorIconValidationTests` |
| `PUT /activity-role/{id}` | ⚠️ | ✅ over-length icon 400, row untouched | ⚠️ | `ActivityEndpointTests:233`; `NameTextColorIconValidationTests` |
| `DELETE /activity-role/{id}` | ✅ | ✅ 409 on system key; ✅ 409 when activities exist (incl. archived) | ✅ | `ActivityRoleSystemKeyTests:166,186` · `ActivityLookupDeleteCascadeTests` |
| `GET /activity-role` (GetAll) | ✅ | — | ✅ scoping + 401 | `CoreGetAllEndpointTests` |
| `GET /activity-role/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:201` |
| ~~`GET /activity-role/by-Name/{v}`~~ | — | — | — | route deleted 2026-09-01 · `TEST-12` |
| `GET /activity-role/by-system-key/{v}` | ✅ | ✅ rename, unknown key, no-match, cross-user | ✅ | `ActivityRoleSystemKeyTests` (9) |
| `GET /activity-role/all-options` | ✅ | ✅ unaffected by archiving | ⚠️ | `ActivityArchivingTests:300`; `ActivityEndpointTests:265` |
| `GET /activity-role/filtered-table` | ⚠️ | ✅ case-insensitive name filter; ❌ `text` / `icon` predicates untested | ⚠️ | `ActivityEndpointTests:216`; `GridTextFilterTests` |
| `POST /activity-category` | ✅ | ✅ dup name 409; ✅ icon length 50/51/60 | ⚠️ | `ActivityEndpointTests:300,859`; `NameTextColorIconValidationTests` |
| `PUT /activity-category/{id}` | ⚠️ | ✅ over-length icon 400, row untouched | ⚠️ | `ActivityEndpointTests:309`; `NameTextColorIconValidationTests` |
| `DELETE /activity-category/{id}` | ✅ | ✅ SetNull keeps the activity + its history; archived too | ✅ | `ActivityEndpointTests:326` · `ActivityLookupDeleteCascadeTests` |
| `POST /activity-category/batch-delete` | ✅ | ✅ foreign id 404; ✅ SetNull in bulk | ✅ | `ActivityEndpointTests:357,371` · `ActivityLookupDeleteCascadeTests` |
| `GET /activity-category` (GetAll) | ✅ | — | ✅ scoping + 401 | `BaseGetAllEndpointTests.cs`; `CoreGetAllEndpointTests` |
| `GET /activity-category/{id}` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:277` |
| `GET /activity-category/all-options` | ⚠️ | ❌ | ⚠️ | `ActivityEndpointTests:341` |
| `GET /activity-category/filtered-table` | ✅ | ✅ role join + RoleSortKey redirect; ✅ case-insensitive name filter | ⚠️ | `BaseFetchTableEndpointTests:69,85,138,154,173`; `GridTextFilterTests` |
| `POST /timer-preset` | ✅ | ✅ duration ≤ 0 400; ✅ foreign `activityId` 404 | ✅ | `TimerPresetCrudTests:106`; `TimerPresetValidationTests:16`; `CoreForeignKeyOwnershipTests` |
| `PUT /timer-preset/{id}` | ✅ | ✅ full overwrite | ✅ | `TimerPresetCrudTests:130`; `…AuthMatrix:121` |
| `DELETE /timer-preset/{id}` | ⚠️ | ✅ cascade from activity | ✅ | `TimerPresetCrudTests:147`; `…AuthMatrix:136` |
| `GET /timer-preset` (GetAll) | ✅ | ✅ caller's own only | ✅ | `TimerPresetCrudTests:163`; `…AuthMatrix:93` |
| `GET /timer-preset/{id}` | ✅ | ✅ | ✅ | `TimerPresetCrudTests:106`; `…AuthMatrix:87` |
| `POST /pomodoro-timer-preset` | ✅ | ✅ focus ≤ 0, empty name; ✅ foreign `activityId` 404 on **both** columns | ✅ | `TimerPresetValidationTests:26,42`; `CoreForeignKeyOwnershipTests` |
| `PUT /pomodoro-timer-preset/{id}` | ✅ | ✅ full overwrite | ✅ | `TimerPresetCrudTests:232`; `…AuthMatrix:205` |
| `DELETE /pomodoro-timer-preset/{id}` | ⚠️ | ✅ cascade from focus activity | ✅ | `TimerPresetCrudTests:264`; `…AuthMatrix:220` |
| `GET /pomodoro-timer-preset` (GetAll) | ✅ | ✅ caller's own only | ✅ | `TimerPresetCrudTests:281`; `…AuthMatrix:161` |
| `GET /pomodoro-timer-preset/{id}` | ✅ | ✅ | ✅ | `TimerPresetCrudTests:191`; `…AuthMatrix:155` |
| `BaseActivityFormSelectOptionsEndpoint<T>` | ✅ | ✅ `?includeArchived=` both ways | ❌ | `ActivityArchivingTests:255` |

Cross-cutting, not per-route: `CoreRouteSmokeTests` (2) pins Core's assembly in FastEndpoints'
`o.Assemblies` and the exactly-once DI scan; `ExtensionToken_CannotAccess_ActivityEndpoint_Returns403`
(`ActivityEndpointTests:1214`) and the three extension rows in `CoreEndpointAuthMatrixTests` are the
extension-client assertions in the area; `CoreEndpointAuthMatrixTests` (29) is the area's 401/role
matrix (`TEST-10`).

### Services, seams, seeders, configuration

| Unit | Tests | Notes |
|---|---|---|
| `ActivityReferenceService` | ~9, indirect | Exercised through `ActivityArchivingTests:353,386,409` and all 13 `ActivityMergeTests`. No direct test, but `CountByActivityAsync` on an **archived** activity — the hole — is now pinned from both read paths by `ArchivedActivityReferenceCountTests` (`TEST-4`, closed 2026-09-08). |
| `TimerPresetActivityReferenceSource` | 2, indirect | `Merge_RepointsEveryReferencingSlice`, `Merge_ReportsWhatItDid`. The two-columns-on-one-row double count is covered by `Merge_OneRowReferencingTwoMergedActivities_CollapsesToOneReference:196`. |
| `UserDefaultsService` | 3, indirect | Through `DefaultUsersSeederTests:233,152,123`. No test of the failure path returning `Result.Failed`. |
| `UserTimeZoneResolver` | 4 direct + 3 indirect | `UserTimeZoneResolverTests` (2026-09-08) pins the three claims the class exists for: an unknown id resolves to `TimeZoneInfo.Utc` and **not** `TimeZoneInfo.Local` (a no-op on a UTC agent and the whole point on a developer box — don't delete that line), a stored zone round-trips through `UserEntityConfiguration`'s id-string conversion, two users in one scope get two zones and the first is not evicted, and a non-ambient user still resolves. The three dashboard suites remain the indirect coverage, all with the zone set. |
| `SeedUserIdProvider` | 1 | `DefaultUsersSeederTests:290` (null when `ROOT_ADMIN_EMAIL` unset). |
| `DefaultUsersSeeder` | 12 | Best-covered unit in Core. Create/override/idempotence/role/PII all pinned. |
| `DefaultActivityRoleSeeder` · `TimerPresetSeeder` · `PomodoroTimerPresetSeeder` | 14 (theory-driven) + 9 matcher | `PerUserDefaultSeederTests` + `PerUserDefaultMatcherTests`. Idempotence, gap-refill, reset-in-place, rename survival — thorough. |
| Dev seeders (`ActivityRole`/`Category`/`Activity`) | 4 | `DevSeederTests` (since 2026-09-08). The app-wide truncate, its schema qualification, the 20/30/40 FK ordering, and `ActivitySeeder`'s no-roles early return. |
| Seam surface | 6 | `SeamWiringTests`. |
| EF configuration | 4 | `ActivityForeignKeyInventoryTests` — now **two** inventories, FKs *into* Activity and (since `SEC-1`, 2026-09-01) FKs *out of* it; `ModuleSchemaTests`, `NamedQueryFilterTests`. |
| Validators | 5, indirect + 11 direct | `TimerPresetValidationTests` + the two 400 paths in `ActivityEndpointTests`; the never-invoked `TimeDtoValidator` (`CQ-2`) is now covered directly by `TimeDtoRangeValidationTests` (since 2026-09-01; the Tracking theory added 2026-09-08 —
the fix reached all three consumers, History, Planning and Tracking's shared `ApplyDateRangeRules`, and only
the first two had a case). The Icon-length mismatch (`CQ-1`) is fixed and pinned from both sides by `NameTextColorIconValidationTests` (8 cases, 2026-09-08) — 50 accepted and stored whole, 51 and 60 rejected, on `POST` and `PUT` × role and category. Each rejection asserts the **error key**, not the status: a Postgres 22001 reaching the client is also a 4xx, so a status-only assertion would pass while the form has no input to attach the message to. |
| `PortalRoleCatalog` · `PortalAuthorizationPolicies` | indirect | Through every role-gated test; `Root` assignment pinned at `DefaultUsersSeederTests:233`. |

### The honest number

**≈ 72% ship-worthy coverage at the 2026-08-31 snapshot; ≈ 88% as of 2026-09-08.** By route today:
39 endpoints, 30 with genuine behavioural assertions, 9 base-class-only, none untested. Twelve of the
fourteen backlog items are closed; what is left is `⚠️` rows where a route exists and returns the
right status and nothing asserts what it *did* — mostly the lookup get-by-id and all-options reads,
where the risk is genuinely low.

Worth recording about the closing pass itself: five of the seven suites added on 2026-09-08 were
written against fixes that had already shipped, so they went green on the first run. The sixth found a
live defect the fix had introduced — see `PERF-2` below — which is the argument for writing the test
even when the finding is marked fixed.

**The riskiest gaps, in order:**

1. ~~**Lookup deletion.**~~ ✅ **Closed 2026-09-01.** `DELETE /activity-category/{id}`, its batch
   sibling and `DELETE /activity-role/{id}` were covered only by base-class tests that delete an
   *unused* row. Nothing asserted what happens when the row is in use, and the answer (`SEC-1`) was
   that every activity under it is destroyed, and through the activity cascade every history row,
   planner task, to-do item and profile that pointed at those activities. This was the single largest
   gap in Core and it was invisible because a green delete test sat next to it.
   `ActivityLookupDeleteCascadeTests` is the guard, and the model half is a second frozen inventory in
   `ActivityForeignKeyInventoryTests`.
2. ~~**Write-side FK ownership** (`SEC-2`).~~ ✅ **Closed.** Five request DTOs copied caller-supplied
   FK ids straight into columns with no ownership check, while the suite's entire notion of "auth" was
   read-side. The endpoints now check visibility (`AllVisibleAsync<T>`) and answer 404, matching
   `MergeActivityEndpoint` so a foreign id stays indistinguishable from one that does not exist;
   `CoreForeignKeyOwnershipTests` is the guard, and it asserts on rows read back with
   `IgnoreQueryFilters()` rather than on the status.
3. **The `%` / `_` escape on the grids' `ILIKE` filters** (`PERF-2`, second order). Fixed 2026-09-08
   and now pinned, but it is the live example of why a fix needs its own test: switching
   `string.Contains` to `EF.Functions.ILike` moved the escaping from EF's hands into
   `FreeTextFilterExtensions.ToContainsPattern`, and the two-argument `ILike` overload emits
   `ILIKE @pattern ESCAPE ''` — an **empty** ESCAPE, which disables escaping rather than falling back
   to Postgres' default backslash. Every `\%` the helper inserted then meant "a literal backslash", so
   any filter containing `%`, `_` or `\` matched **nothing at all**: an empty grid, no error, the same
   symptom as the case-sensitivity bug the switch was made to fix. The escape character is now passed
   explicitly at all nine call sites via `FreeTextFilterExtensions.EscapeCharacter`.
4. ~~**The archival filter inside the reference seam** (`SEC-4`).~~ ✅ **Closed 2026-09-08.**
   `usageCount` on an archived activity omitted its profile rows, so `canDelete` came back `true` on
   the one screen whose delete route lifts the archival filter on purpose — and a merge into an
   archived survivor that already held a profile repointed a second row onto a unique `ActivityId` and
   rolled the whole merge back. The fix shipped 2026-09-01; the pins are
   `ActivityArchivingTests.UsageCount_CountsTheProfilesOfAnArchivedActivity` (both read paths),
   `ActivityMergeTests.Merge_IntoAnArchivedSurvivorThatAlreadyHasAProfile_KeepsTheSurvivorsProfile`,
   and `ArchivedActivityReferenceCountTests` for what the count was buying — the delete it withholds
   really does cascade the profile away — and for the active-activity control that stops a fix from
   double-counting.

## 12. Missing tests — backlog

Prompt files live in [`testingPrompts/`](testingPrompts/) and are paste-ready for a context-less
agent. Read [`testingPrompts/_SHARED-CONTEXT.md`](testingPrompts/_SHARED-CONTEXT.md) first — it holds
the fixture, auth-client and `KnownGap` conventions the individual prompts assume.

| ID | Gap | Finding | Prompt |
|---|---|---|---|
| `TEST-1` ✅ closed 2026-09-01 | Deleting a role or category destroys every activity under it, and their cross-slice rows | `SEC-1` | [ActivityLookupDeleteCascade.md](testingPrompts/ActivityLookupDeleteCascade.md) → `Endpoints/ActivityLookupDeleteCascadeTests.cs` + `Modules/ActivityForeignKeyInventoryTests.cs` |
| `TEST-2` ✅ closed | Core writes accept another user's `roleId` / `categoryId` / `activityId` | `SEC-2` | [CoreForeignKeyOwnership.md](testingPrompts/CoreForeignKeyOwnership.md) → 7 methods / 8 cases in `Endpoints/CoreForeignKeyOwnershipTests.cs` |
| `TEST-3` ✅ closed 2026-09-01 | `POST /activity/merge` 404s when a merged id is archived | `SEC-3` | [MergeArchivedMergedId.md](testingPrompts/MergeArchivedMergedId.md) → 4 tests in `Endpoints/ActivityMergeTests.cs` |
| `TEST-4` ✅ closed 2026-09-08 | `usageCount` on an archived activity omits filtered dependents; merge into an archived survivor holding a profile | `SEC-4` | [ArchivedActivityReferenceCount.md](testingPrompts/ArchivedActivityReferenceCount.md) → 3 tests in `Endpoints/ArchivedActivityReferenceCountTests.cs`, plus `UsageCount_CountsTheProfilesOfAnArchivedActivity` + `FilteredTable_DefaultView_StillHidesArchived_…` in `Endpoints/ActivityArchivingTests.cs` and `Merge_IntoAnArchivedSurvivorThatAlreadyHasAProfile_…` in `Endpoints/ActivityMergeTests.cs` |
| `TEST-5` ✅ closed 2026-09-08 | An `icon` of 51–255 chars passes validation and fails in Postgres | `CQ-1` | [IconLengthValidatorVsColumn.md](testingPrompts/IconLengthValidatorVsColumn.md) → 8 cases in `Endpoints/NameTextColorIconValidationTests.cs` |
| `TEST-6` ✅ closed 2026-09-01 | `TimeDto` hours/minutes unvalidated on the requests Planning does not hand-check | `CQ-2` | [TimeDtoRangeValidation.md](testingPrompts/TimeDtoRangeValidation.md) → 15 cases in `Endpoints/TimeDtoRangeValidationTests.cs` (11 on 2026-09-01, plus 4 for Tracking on 2026-09-08) |
| `TEST-7` ✅ closed 2026-09-08 | Quick-edit and clone 404 on an archived activity while update and get-by-id do not | `CQ-3` | [QuickEditCloneArchived.md](testingPrompts/QuickEditCloneArchived.md) → 3 tests in `Endpoints/ActivityArchivingTests.cs`. The fix had already shipped: both endpoints lift `AppQueryFilters.ActivityArchived` with a keyed `FirstOrDefault` (not `FindAsync`, which cannot lift one) and `CloneActivityEndpoint` resets `IsArchived` — so the product question the 404 was hiding is answered "the copy starts **active**". The foreign-archived case pins that only the archival filter was lifted, never the per-user one. |
| `TEST-8` ✅ closed 2026-09-08 | `Activity.Clone()` copies the source's created/modified timestamps | `CQ-4` | [CloneTimestamps.md](testingPrompts/CloneTimestamps.md) → 2 tests in `Endpoints/ActivityEndpointTests.cs` (`ActivityAreaBehaviorTests`, section F) |
| `TEST-9` ✅ closed 2026-09-08 | Grid `Contains` filters are case-sensitive against a Postgres `LIKE` | `PERF-2` | [GridFilterCaseSensitivity.md](testingPrompts/GridFilterCaseSensitivity.md) → 4 tests (8 cases) in `Endpoints/GridTextFilterTests.cs`. **The suite found a live defect in the shipped fix**: the two-argument `EF.Functions.ILike` emits `ESCAPE ''`, which disables escaping rather than defaulting to backslash, so any filter containing `%`, `_` or `\` matched nothing at all. Fixed by passing `FreeTextFilterExtensions.EscapeCharacter` at all nine call sites. The metacharacter case seeds decoy rows (`1005 Focus`, `deepXwork`) so the opposite failure — an unescaped wildcard — shows up as a wrong row rather than a merely generous result. |
| `TEST-10` ✅ closed 2026-09-08 | No 401 assertion on any Core route; the four hand-rolled endpoints rely on the global configurator | `CQ-3` | [CoreEndpointAuthMatrix.md](testingPrompts/CoreEndpointAuthMatrix.md) → 29 cases in `Endpoints/CoreEndpointAuthMatrixTests.cs` |
| `TEST-11` ✅ closed 2026-09-08 | `GET /activity`, `/activity-role`, `/activity-category` (the GetAll routes — **not** `…/all`, see the prompt) had no test at all | — | [GetAllEndpointCoverage.md](testingPrompts/GetAllEndpointCoverage.md) → `Endpoints/CoreGetAllEndpointTests.cs` |
| `TEST-12` ✅ closed 2026-09-01 | `GET /activity-role/by-Name/{v}` was routed and untested — endpoint deleted instead, no test written | `CQ-7` | [GetAllEndpointCoverage.md](testingPrompts/GetAllEndpointCoverage.md) |
| `TEST-13` ✅ closed 2026-09-08 | The "missing user ⇒ UTC, never `TimeZoneInfo.Local`" fallback is asserted nowhere | `CQ-10` | [UserTimeZoneResolverFallback.md](testingPrompts/UserTimeZoneResolverFallback.md) → 4 tests in `Infrastructure/UserTimeZoneResolverTests.cs`. The non-UTC zone is picked from the running platform's database rather than hard-coded, so the suite does not depend on Windows vs. ICU zone ids. |
| `TEST-14` ✅ closed 2026-09-08 | Dev seeders truncate the whole table for every user; nothing pins that | `CQ-11` | [DevSeederTruncateScope.md](testingPrompts/DevSeederTruncateScope.md) → 4 tests in `Seeding/DevSeederTests.cs`. The prompt's "give this class its own collection" precaution is unnecessary — Respawn resets the database before every test — so these execute the seeders for real rather than asserting on their contract. |
