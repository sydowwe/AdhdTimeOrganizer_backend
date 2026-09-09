# AdhdTimeOrganizer.TodoLists — 01 Testing

Point-in-time snapshot, 2026-08-31. Disposable. The durable map is
`AdhdTimeOrganizer.TodoLists/docs/domain-map.md`.

**Recheck, pass 2 (2026-08-31).** No test file in the suite has changed since the first pass — the
newest TodoLists-related test file is `ActivityMergeTests.cs` (2026-08-27), and the last commit to
`AdhdTimeOrganizer.IntegrationTests` (`03547ed`, 2026-08-30) touched the category grid, not this slice.
The coverage matrix and the ~62% number below are re-confirmed as-is. Also confirmed: **there is not a
single `[Trait("Status", "KnownGap")]` anywhere in the test project**, so nothing here is
already-pinned-as-known; every ❌ is a genuine absence. Two changes in this pass: `TEST-10`'s boundary is
the **102nd** item, not the 101st (see the corrected `CQ-2`), and a new `TEST-14` covers `CQ-19`.

## Test infrastructure summary

There is no `AdhdTimeOrganizer.TodoLists.Tests` project — every test for this slice lives in
`AdhdTimeOrganizer.IntegrationTests`, which pins the *host's* composition and therefore exercises the
slice through real HTTP against a real Postgres. The shape is uniform: `[Collection("Postgres")]` +
`PostgresTestBase(AppDbContextFixture)` from `Sydowwe.Framework.Testing`, which hands each test
`CreateClient()` / `CreateUserRoleClient()` (auth is faked at the handler, `FakeLoggedUserService.TestUserId`),
`CreateDbContext()` for out-of-band seeding and verification, `JsonOpts` and a `CancellationToken`.
Fixtures for this area are centralised in `AdhdTimeOrganizer.IntegrationTests/Endpoints/TodoListTestSeedHelper.cs`,
which deliberately delegates activity seeding to `PlanningTestSeedHelper.SeedActivityAsync` rather than
duplicating it, and which also carries the *Routines* seeders (`SeedRoutineTodoListAsync`,
`SeedRoutineTimePeriodAsync`) because the six shared endpoint bases are exercised from both sides.
Cross-user scenarios reuse `ReminderSeedHelper.OtherUserId` / `EnsureOtherUserAsync`.

The auth matrix is generated rather than written: `TodoListCrudAuthMatrixTests.cs` declares 24 tiny
subclasses of the framework's `BaseCreateEndpointTests` / `BaseUpdateEndpointTests` /
`BaseDeleteEndpointTests` / `BaseGetByIdEndpointTests`, one per (entity × verb), and each supplies only
a route, a payload factory and a seeder. That is the strongest thing in this suite — it means adding an
entity to the slice costs ~15 lines to get 401 / cross-user-404 / User-role coverage.

**Fragile or bespoke, worth knowing before you add tests here:**

- `TodoListItem` carries a unique index on `(UserId, ActivityId, TodoListId)`. `SeedTodoListItemAsync`
  does not defend against it, so two seeded items sharing an activity *and* a list collide at
  `SaveChangesAsync` with an unrelated-looking Postgres error. Most existing tests dodge this by seeding
  a fresh activity per item.
- `TaskPriority` is unique on `(UserId, Priority)` across the whole test user, and the collection shares
  one database. `TodoListStepsCrudTests.SeedItemAsync` works around this with
  `Random.Shared.Next(1000, 999_000)` — an unseeded random in a shared-DB suite, so it is a real (if
  low-probability) flake source. `TaskPriorityReorderTests` instead hard-codes ranks 1 and 2, and
  `TodoListDeleteBehaviorTests` uses 700; nothing coordinates these.
- `TodoListItemCompletionInterceptor` is registered host-side in `Program.cs` and stamps
  `CompletedTimestamp` from the ChangeTracker. Any test that seeds an item with `IsDone = true` through
  `CreateDbContext()` gets a stamp of "now" for free, which is why
  `TodoListItemDailyRecapTests` has its own timestamp-setting helper rather than using the shared seeder.
- `TestContext.Current.CancellationToken` and the base's `CancellationToken` property are used
  interchangeably within the same file (e.g. `TodoListDeleteBehaviorTests`), which is noise rather than a
  defect but makes diffs harder to read.

## 7. Test quality assessment

**Strengths — genuinely above average for this repo.**

*Assertions are behavioural, not structural.* `TodoListItemDailyRecapTests.cs` is the model: its class
comment names the three mechanisms that fail silently (interceptor registration, the cross-slice
`ITodoListItemLoggedTimeSource` seam, the host-side `SetNull` FK) and every test asserts on a row or a
payload rather than on a status code. The double-count test (`:251`) is the best single test in the
slice — two items on one activity, time logged against only the completed one — because it fails
precisely if someone "helpfully" widens the seam to activity matching, which is the exact regression
`docs/summary.md` warns about. `TodoListItemPairingTests.cs` does the same for
`PairedLeisureActivityId`, and its comment (`:14-28`) correctly identifies that a dropped projection
field reads back as `null` and is therefore indistinguishable from "not paired".

*Tests that disagree with the spec say so, in the DisplayName.* `TodoListStepsCrudTests` was written
against a spec that assumed `Steps` and `TotalCount` were coupled; they are not, and rather than
quietly writing weaker assertions the file pins the *actual* decoupled behaviour and names the finding
in each `DisplayName`
(`"Creating steps well past 99 succeeds cleanly -- there is no cap tied to the Steps collection"`).
Same with `TaskPriorityReorderTests`, which pins that the only way to reorder over this API is a
three-step dance through a temporary rank, and says so. That is documentation-by-test done right.

*One real invariant fuzz test exists.* `TodoListToggleAndDoneCountTests:171` runs a seeded-random toggle
sequence and asserts `0 <= DoneCount <= TotalCount` throughout. `IsDoneLogic` has six branches and is
the most arithmetic-dense method in the slice; this is the right tool for it.

**Weaknesses.**

*Coverage is concentrated on the two most recently built features.* Daily recap (11 tests) and pairing
(6) are the newest surfaces and are near-exhaustively covered. The reorder/move machinery — which is
where the actual defects are (CQ-2 … CQ-6) — has exactly one test, and it is an IDOR test
(`ChangeDisplayOrderTodoListItemIdorTests`), not a behavioural one. **No test ever produces a
`DisplayOrder` value and asserts on it.** The rebalance branch of `BaseChangeDisplayOrderTodoListEndpoint`
has never been executed by any test, which is how CQ-4 (an untranslatable LINQ expression on that path)
survived.

*A minor case of assertion theater.* `TodoListsRouteSmokeTests` asserts `StatusCode.Should().NotBe(404)`
over three routes. That is the right assertion for what it claims to test (FastEndpoints assembly
registration) and its comment justifies the scope honestly — but three routes out of 34 is thin for a
guard whose stated failure mode is "every to-do route silently 404s". A 400 from a badly-formed request
also passes it.

*Isolation is by convention, not by construction.* Every test shares one database and one test user;
separation comes from per-test GUID-suffixed names. It holds today, but the `Random.Shared` priority
above and the unique index on `(UserId, ActivityId, TodoListId)` mean a new test that reuses an activity
can fail a *different* test's seeding. There is no per-test transaction rollback.

*AAA structure is clean* — the `await using (var db = CreateDbContext())` seed block / act / `verifyDb`
assert block pattern is followed almost everywhere, and verification always happens on a **fresh**
context, so nothing passes on change-tracker identity alone. That detail is correct across all eight
files and is worth preserving.

## 8. Coverage matrix

### Endpoints

Counts are tests that meaningfully exercise that endpoint (not incidental use as a fixture).

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /todo-list` (Create) | ✅ 1 | ❌ `TEST-11` | ✅ 3 | `TodoListCrudAuthMatrixTests:27`, `:577` |
| `PUT /todo-list/{id}` (Update) | ✅ 1 | ❌ `TEST-11` | ✅ 3 | `TodoListCrudAuthMatrixTests:39` |
| `DELETE /todo-list/{id}` | ✅ 1 | ❌ cascade-to-items unasserted `TEST-12` | ✅ 3 | `TodoListCrudAuthMatrixTests:61` |
| `GET /todo-list/{id}` | ✅ 1 | ❌ | ✅ 3 | `TodoListCrudAuthMatrixTests:78` |
| `POST /todo-list/filter-sort` | ❌ | ❌ `CategoryId == -1` path | ❌ | — `TEST-7` |
| `GET /todo-list/all-options` | ⚠️ smoke only | ❌ | ❌ | `TodoListsRouteSmokeTests` — `TEST-7` |
| `POST /todo-list-category` | ✅ 1 | ❌ | ✅ 3 | `TodoListCrudAuthMatrixTests:95` |
| `PUT /todo-list-category/{id}` | ✅ 1 | ❌ unique `(UserId,Name)` | ✅ 3 | `:108` |
| `DELETE /todo-list-category/{id}` | ✅ 1 | ✅ SetNull to lists | ✅ 3 | `:131`, `TodoListDeleteBehaviorTests:61` |
| `GET /todo-list-category/{id}` | ✅ 1 | ❌ | ✅ 3 | `:148` |
| `POST /todo-list-category/filter-sort` | ❌ | ❌ synthetic `Id = -1` row, `HideEmpty` | ❌ | — `TEST-6` |
| `GET /todo-list-category/all-options` | ❌ | ❌ | ❌ | — `TEST-7` |
| `POST /task-priority` | ✅ 1 | ❌ unique `(UserId,Priority)` on create | ✅ 3 | `:165` |
| `PUT /task-priority/{id}` | ✅ 1 | ✅ 409 on rank collision | ✅ 3 | `:213`, `TaskPriorityReorderTests` |
| `DELETE /task-priority/{id}` | ✅ 1 | ✅ Restrict → 409 | ✅ 3 | `:179`, `TodoListDeleteBehaviorTests:40` |
| `GET /task-priority/{id}` | ✅ 1 | ❌ | ✅ 3 | `:196` |
| `GET /task-priority` (GetAll) | ⚠️ smoke only | ❌ | ❌ | `TodoListsRouteSmokeTests` — `TEST-7` |
| `GET /task-priority/all-options` | ❌ | ❌ | ❌ | — `TEST-7` |
| `POST /task-priority/grid` | ❌ | ❌ all four filter arms | ❌ | — `TEST-7` |
| `POST /todo-list-item` (Create) | ✅ 1 | ⚠️ steps/TotalCount only | ✅ 3 | `TodoListCrudAuthMatrixTests:237` |
| ↳ cross-user `TodoListId` / `TaskPriorityId` | — | — | ❌ **`TEST-8`** | — |
| ↳ `DisplayOrder` assignment | ❌ **`TEST-10`** | ❌ 102nd-item boundary | — | — |
| `PUT /todo-list-item/{id}` (Update) | ✅ 1 | ✅ pairing round-trip | ✅ 3 | `:257`, `TodoListItemPairingTests:123` |
| ↳ step identity survives the PUT | ❌ **`TEST-14`** | — | — | — |
| `DELETE /todo-list-item/{id}` | ✅ 1 | ✅ history `SetNull` survives | ✅ 3 | `:293`, `TodoListItemDailyRecapTests:316` |
| `GET /todo-list-item/{id}` | ✅ 1 | ✅ pairing projected | ✅ 3 | `:318`, `TodoListItemPairingTests:85` |
| `GET /todo-list-item` (GetAll) | ⚠️ smoke + 1 incidental | ❌ `todoListId` filter, ordering | ❌ | `TodoListItemPairingTests:85` — `TEST-4` |
| `GET /todo-list-item/dashboard-widget` | ❌ | ❌ 3-day window, server-vs-user TZ | ❌ | — **`TEST-5`** |
| `GET /todo-list-item/daily-recap` | ✅ 4 | ✅ 5 (UTC edges, zero-day, decline, double-count) | ✅ 1 | `TodoListItemDailyRecapTests` |
| `PATCH /todo-list-item/toggle-is-done` | ✅ 3 | ✅ 4 incl. fuzz | ⚠️ via matrix only | `TodoListToggleAndDoneCountTests` |
| `PATCH /todo-list-item/{id}/steps/{id}/toggle` | ✅ 2 | ✅ 1 (untick a fully-done item) | ❌ | `TodoListToggleAndDoneCountTests:99`, `:131` |
| `PATCH /todo-list-item/change-display-order` | ✅ 4 (all three arithmetic cases + the no-neighbours one) | ✅ 4 (both rebalance outcomes, unknown neighbour ids, completion state) | ✅ 2 | `ChangeDisplayOrderTodoListItemTests`, `TodoListCrudAuthMatrixTests:525` |
| `PATCH /todo-list-item/{id}/priority` | ❌ **`TEST-2`** | ❌ cross-group `DisplayOrder` | ❌ | — |
| `PATCH /todo-list-item/{id}/move` | ❌ **`TEST-1`** | ❌ the 409 the endpoint hand-maps | ❌ | — |
| `POST /todo-list-item/{id}/steps` | ✅ 2 | ✅ 1 (101 steps) | ⚠️ via update-step | `TodoListStepsCrudTests` |
| `PUT /todo-list-item/{id}/steps/{id}` | ✅ 1 | ❌ | ✅ 1 | `TodoListStepsCrudTests:84`, `:133` |
| `DELETE /todo-list-item/{id}/steps/{id}` | ✅ 1 | ✅ 1 (no floor) | ❌ | `TodoListStepsCrudTests:111` |

### Services / handlers / jobs / seeders / seams

| Component | Tests | Where |
|---|---|---|
| `TodoListItemCompletionInterceptor` | ✅ 3 (stamp on transition, no re-stamp on edit, clear on un-done) | `TodoListItemDailyRecapTests:116`, `:157` |
| `TodoListActivityReferenceSource` — repoint | ✅ 2 | `ActivityMergeTests:125`, `:196` |
| ↳ two items → one survivor on one list | ❌ **`TEST-9`** — the `(UserId, ActivityId, TodoListId)` collision | — |
| `TodoListActivityMembershipSource` | ⚠️ wiring only | `SeamWiringTests` |
| `TaskPrioritySeeder` (userDefault, 100) | ✅ 3 | `PerUserDefaultSeederTests:76`, `:262`, `:288` |
| `TodoListItemSeeder` (dev, 100) | ❌ `TEST-13` | — |
| `TodoListExtensions.GetNextDisplayOrder` | ❌ **`TEST-10`** | — |
| `TodoListExtensions.GetDisplayOrderById` / `GetGroupIdById` | ⚠️ indirectly, via the IDOR test | `TodoListCrudAuthMatrixTests:525` |
| `BaseTodoListItem.SetDone` | ✅ via fan-out | `CompletionFanOutTests:82` |
| `TodoListSettings` binding | ❌ — nothing asserts `MaxItems`/`DisplayOrderGap` reach the endpoints | — `TEST-10` |
| Slice registration (FastEndpoints assemblies) | ✅ 1 (3 routes) | `TodoListsRouteSmokeTests` |
| Schema placement (`todo`) | ✅ | `Infrastructure/ModuleSchemaTests` |
| Named query filters | ✅ | `Infrastructure/NamedQueryFilterTests` |
| GDPR export / erasure | ✅ | `UserDataExportAndDeletionTests:88` |

### The honest number

**You can ship this at ~62% meaningful coverage.**

Weighting by endpoint rather than by line: 22 of 34 routes have at least one real behavioural
assertion, and the auth/IDOR matrix is effectively complete for the 16 plain-CRUD routes. But the
number flatters the slice, because the coverage is not distributed where the risk is:

- **The riskiest gap is the ordering machinery.** `change-display-order`, `move`, `priority` and
  `GetNextDisplayOrder` are four surfaces that mutate the same column, share the same arithmetic, and
  between them have **one** test — which asserts a 404. *(Amended 2026-09-09: three of the four are now
  covered — `GetNextDisplayOrder` by `TodoListDisplayOrderMintingTests`, `move`/`priority` by
  `TodoListReferencedIdOwnershipTests`, and `change-display-order` by
  `ChangeDisplayOrderTodoListItemTests`. The ~62% headline below is stale in the slice's favour and has
  not been recomputed.)* Three of the five 🟠 findings in `02-findings.md`
  live on those paths, and one of them (CQ-4) means the rebalance branch throws at runtime and has
  simply never been reached. Read paths that were never exercised are the usual suspect in this repo;
  here it is a *write* path.
- **Second is the read surface.** Six query endpoints (`filter-sort` ×2, `grid`, `all-options` ×3,
  `dashboard-widget`, `GetAll`) have no behavioural test at all. `dashboard-widget` additionally uses
  `DateTime.Today` — server-local — in a codebase that has dedicated user-zone tests for every other
  dashboard (`HistoryDashboardUserZoneTests`, `TrackingDashboardUserZoneTests`).
- **Third is FK ownership on create.** The IDOR matrix proves you cannot *read* another user's row. It
  does not prove you cannot *point at one*, and `CreateTodoListItemEndpoint` accepts `TodoListId` and
  `TaskPriorityId` unvalidated where `MoveTodoListItemEndpoint` explicitly checks them.
- **Fourth, added in the recheck: no test crosses two endpoints on one item.** Every file exercises one
  route per scenario. `TodoListStepsCrudTests` drives the step routes and `TodoListItemPairingTests:123`
  drives the PUT, but nothing does a PUT *and then* uses a step id — which is exactly why `CQ-19` (the
  PUT regenerating every step `Guid`) is invisible to a suite this size. Sequence tests are the cheapest
  thing missing here.

## 12. Missing tests — backlog

Prompts are paste-ready and self-contained; each names the route, the file to create, fixtures to reuse,
and the KnownGap convention. They live beside this review because they are generated from it and are
disposable — the real tests belong in `AdhdTimeOrganizer.IntegrationTests`.

> **2026-09-09:** `TEST-1`, `TEST-2` and `TEST-8` are **partly landed** as
> `AdhdTimeOrganizer.IntegrationTests/Endpoints/TodoListReferencedIdOwnershipTests.cs` (12 cases),
> written alongside the `SEC-1`/`SEC-2`/`SEC-3`/`CQ-6`/`CQ-7` fixes. It covers the ownership half of all
> three, the unique-index 409 on move, and the `DisplayOrder` recompute on both patches. The rest of
> each prompt — the validation and no-op cases, and `TEST-2`'s untouched-siblings assertion — is still
> open; the prompts stand.
>
> **Also 2026-09-09:** `TEST-14` is **landed in full** as `TodoListItemUpdateOwnedFieldsTests.cs` (10
> cases), with the `SEC-5`/`CQ-9`/`CQ-16`/`CQ-19`/`DOC-6` batch. It closes the recheck's fourth blind
> spot above — "no test crosses two endpoints on one item" — by doing a PUT and *then* using a step id,
> which is the sequence the whole finding turned on. `TodoListToggleAndDoneCountTests` also gained one
> case for `CQ-9`'s boundary; note that the overshot row the finding asks it to seed cannot exist (the
> check constraint refuses it), so the test pins `DoneCount == TotalCount` with `IsDone` false instead —
> see the `CQ-9` fix note in `02-findings.md`.

| ID | Gap | Sev | Related finding | Prompt |
|---|---|---|---|---|
| `TEST-1` | `PATCH /todo-list-item/{id}/move` — zero tests, incl. the unique-index 409 the endpoint hand-maps | 🟠 | SEC-3, CQ-7 | [MoveTodoListItem.md](testingPrompts/MoveTodoListItem.md) |
| `TEST-2` | `PATCH /todo-list-item/{id}/priority` — zero tests; changes the `DisplayOrder` group without reordering | 🟠 | SEC-2, CQ-6 | [ChangePriorityTodoListItem.md](testingPrompts/ChangePriorityTodoListItem.md) |
| `TEST-3` ✅ **DONE 2026-09-09** | `change-display-order` — 11 tests in `ChangeDisplayOrderTodoListItemTests`, including both rebalance outcomes and the Routines twin | 🟠 | CQ-4, CQ-5, PERF-2 | [ChangeDisplayOrderTodoListItem.md](testingPrompts/ChangeDisplayOrderTodoListItem.md) |
| `TEST-4` | `GET /todo-list-item` — `todoListId` filter and `DisplayOrder` ordering unasserted | 🟡 | PERF-1 | [GetAllTodoListItem.md](testingPrompts/GetAllTodoListItem.md) |
| `TEST-5` | `GET /todo-list-item/dashboard-widget` — zero tests; server-local `DateTime.Today` | 🟠 | CQ-18 | [GetDashboardTodoListItem.md](testingPrompts/GetDashboardTodoListItem.md) |
| `TEST-6` | `POST /todo-list-category/filter-sort` — zero tests; synthetic `Id = -1` row, `HideEmpty` | 🟡 | CQ-8, PERF-4 | [GetFilterSortTodoListCategory.md](testingPrompts/GetFilterSortTodoListCategory.md) |
| `TEST-7` | The five remaining read routes (`todo-list/filter-sort`, `task-priority/grid`, 3× `all-options`) | 🟡 | PERF-5 | [ReadSurfaceSweep.md](testingPrompts/ReadSurfaceSweep.md) |
| `TEST-8` | `POST /todo-list-item` accepts another user's `TodoListId` / `TaskPriorityId` | 🟠 | SEC-1 | [CreateTodoListItemForeignKeyOwnership.md](testingPrompts/CreateTodoListItemForeignKeyOwnership.md) |
| `TEST-9` | Merging two activities that both have an item on one list violates the unique index | 🟠 | CQ-1 | [ActivityMergeTodoListCollision.md](testingPrompts/ActivityMergeTodoListCollision.md) |
| `TEST-10` | `GetNextDisplayOrder` — the 101st item in a priority group lands back at `DisplayOrderStart` | 🟠 | CQ-2, CQ-3 | [GetNextDisplayOrderBoundary.md](testingPrompts/GetNextDisplayOrderBoundary.md) |
| `TEST-11` | `TodoList` / `TodoListCategory` unique `(UserId, Name)` — no 409 test on create or update | 🟡 | — | folded into `ReadSurfaceSweep.md` |
| `TEST-12` | `DELETE /todo-list/{id}` cascades its items — asserted nowhere | 🟡 | — | folded into `ReadSurfaceSweep.md` |
| `TEST-13` | Dev `TodoListItemSeeder` — no test; carries its own copy of the CQ-2/CQ-3 arithmetic | 🟡 | CQ-13 | folded into `GetNextDisplayOrderBoundary.md` |
| `TEST-14` ✅ **DONE 2026-09-09** | 10 tests in `TodoListItemUpdateOwnedFieldsTests`, landed with the `SEC-5`/`CQ-9`/`CQ-16`/`CQ-19`/`DOC-6` batch: step identity across a PUT (incl. the toggle route afterwards and the two-PUT case), the three-way reconcile, and the fields the PUT must not own | 🟠 | CQ-19, CQ-16, SEC-5 | [StepIdentityAcrossUpdate.md](testingPrompts/StepIdentityAcrossUpdate.md) |
