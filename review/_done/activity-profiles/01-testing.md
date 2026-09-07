# AdhdTimeOrganizer.ActivityProfiles — 01 Testing

Point-in-time snapshot, 2026-09-01. First review of this slice — there is no prior
`review/activity-profiles/` pass to delta against. The module's own `docs/testing.md` **is** treated
as prior art: everything it already lists under *Known gaps* is marked "previously known" below and
is not re-flagged as a defect.

## Test infrastructure summary

Everything lives in `AdhdTimeOrganizer.IntegrationTests` — this project has no test project of its
own, and that is deliberate (it pins the host's composition, which is where all four registration
seams live). Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` over the
`"Postgres"` collection with a real container (`AppDbContextFixture`), `CreateClient()` /
`CreateUserRoleClient()` for the authenticated cases, and `UserRoleTestAuthHandler` for role and
anonymous scenarios. Two pure-rule suites (`LeisureDrawRankerTests`, `WeatherFitRuleTests`) need
nothing at all and run as plain xUnit.

Three things here are bespoke and worth knowing before you add a file:

- **The weather doubles** (`LeisureWeatherFitTests:299,310`) — `StubWeatherProvider` and
  `ThrowingWeatherProvider`, injected through `CreateFactory(..., configureServices:)`. This is the
  only way to test the weather surface; the real provider makes an outbound call and no test may
  depend on today's weather in a real place. `ThrowingWeatherProvider` exists specifically because
  `GetLeisureWeatherFitEndpoint` carries its own `catch` **on top of** the provider's never-throw
  promise.
- **The seeding helpers in `ActivityProfileGridTests:33-150`** (`CreateSecondUserAsync`,
  `SeedActivityAsync`, `SeedLookupAsync<T>`, `SeedBacklogProfileAsync`, `SeedBucketListProfileAsync`,
  `SeedProjectProfileAsync`, `SeedMemoryAnchorAsync`). These are `private` to that class — every new
  file in this area currently re-declares its own copies (`LeisureSuggestionTests` has a third set).
  That is the single biggest fragility in the area's test code: three independent definitions of
  "a seeded backlog profile", all of which must stay in step with a schema that has two check
  constraints and four required FKs.
- **`CreateSecondUserAsync` goes through `UserManager`, not the DbContext**, because the per-user
  default seeders fire on sign-up. A hand-inserted second `User` row gets no lookups and the
  cross-user tests then pass for the wrong reason.

Fragile spots: `LeisureSuggestionTests` is a 685-line file that has picked up two tests belonging to
**Planning** (`DoItNow_OnADayWithNoCalendarRow_PlansTheTaskAsUnderWay:478`,
`AStartTimeOnATaskThatHasNotStarted_IsRejected:513`). They are good tests in the wrong file — the
"do it now" flow starts at a leisure card, but it asserts on planner rows and will be looked for in
`review/planning`. No `[Trait("Status","KnownGap")]` exists anywhere in the test project, so the
`--filter Status!=KnownGap` escape hatch is unwired.

## 7. Test quality assessment

**Strengths — this is among the better-tested areas in the solution.**

- **Assertions are on rows and id sets, not on status codes.** `LeisureWeatherFitTests` asserts the
  resolved `matchingWeatherDependencyIds` set rather than a 200 (`:47`, `:68`, `:81`), which is the
  only way to notice a weather signal that has quietly stopped matching — every failure shape of that
  feature is also a 200. `ActivityProfileGridTests` re-reads through a fresh context to prove
  `*_UpdateAndDelete_ForeignProfile_AreRefusedAndRowUnchanged` (`:200`, `:308`, `:391`) actually left
  the row alone rather than merely answering 404.
- **A second user is seeded wherever scoping is the subject** — `Backlog_Grid_OnlyReturnsCallersOwnRows`
  (`:170`) and its two siblings, `BucketList_Grid_AnotherUsersAnchorDoesNotMarkTheEntryDone` (`:643`),
  `AnotherUsersProfiles_AreNeverDrawn` (`LeisureSuggestionTests:385`),
  `AnotherUsersRows_AreNeverReturned` (`LeisureWeatherFitTests:127`). This is the discipline the whole
  slice depends on, and it is followed.
- **The determinism contract is pinned from both ends** — `TheSameSeedOverTheSamePool_...` and
  `TheDrawDoesNotDependOnTheOrderTheRowsArrivedIn` (`LeisureDrawRankerTests:107,118`) as unit tests,
  `ReloadingTheSameSeededDraw_ShowsTheSameCardsInTheSameOrder` (`LeisureSuggestionTests:150`) end to
  end. A reviewer can delete the `ThenBy` tiebreak and something goes red.
- **The pure rules are tested as pure rules.** 19 ranker tests and 8 weather-rule tests, no database,
  one behaviour each, and the `DisplayName`s read as the product rule they encode. `EligibleCount_
  CountsWhatSurvivedTheConstraints_NotWhatWasDrawn` (`:225`) is the kind of test that catches a
  refactor nobody would otherwise notice.
- **Sorting on a derived field is pinned** (`BucketList_Grid_SortsByIsAnchored:546`) — the failure it
  guards (a field overlaid after the query sorts on `false` for every row and the page comes back in
  arbitrary order, with no error) is invisible otherwise.

**Weaknesses.**

- **Assertion theater is rare but present in the smoke file.** `ProfileRoutes_AreRegistered` and
  `LookupAndAnchorGridRoutes_AreRegistered` (`ActivityProfilesRouteSmokeTests:36,53`) assert
  `StatusCode.Should().NotBe(NotFound)` — a route that answered 500 on every call passes. That is
  defensible for a registration smoke test and the class comment says as much, but it means the eight
  routes it names have *no* behavioural coverage from this file, which is easy to misread as coverage.
- **Coverage is shaped like the docs, not like the endpoint list.** Every surface `summary.md` calls
  load-bearing is tested twice over; the surfaces it does not mention (`GetAll` on the backlog, the
  four lookups' `GetAll`, all three batch-deletes, two of the three select-options routes) have
  nothing at all. `SEC-1` in [02-findings.md](02-findings.md) is exactly what that shape lets through:
  an unscoped read that has been documented as "the one using the plain projection" and never as
  "the one nobody scoped".
- **Ownership is proven on the read and update paths and assumed on the delete paths.** There is no
  `*_BatchDelete_ForeignProfile_*` anywhere, and `BaseBatchDeleteEndpoint`'s `AuthorizeAsync` default
  is `true` — for `MemoryAnchor` the global query filter covers it, for the three profiles the
  hand-written loop does, and neither fact is pinned.
- **Three copies of the seeding helpers** (see above). Not a correctness problem today; it is why
  adding a test to this area costs 60 lines before the first assertion.
- **`ActivityProfileGridTests` mixes a fixture-heavy 150-line preamble with 23 tests in one class.**
  Isolation is fine (each test seeds its own activities, and the second user is created per test with
  a unique email), but the file is at the size where a split by profile family would help.

No flakiness risks found: no `DateTime.Now` in an assertion, no ordering assumption on a set, no
sleep, and the two clock-sensitive suites inject their reference instant
(`LeisureRankingContext.Now`, and `AnOldRecord_IsSweptOnTheNextWrite:299` writes the cutoff column
explicitly rather than waiting).

## 8. Coverage matrix

### Endpoints (57)

`Happy` = a green path asserted on rows · `Edge` = validation / empty / boundary · `Auth` = ownership
or anonymous refusal. Counts are test methods that touch the route.

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `POST /activity-backlog-profile` | ✅ 1 | ✅ 1 | ✅ 1 | `ActivityProfileGridTests:250` |
| `PUT /activity-backlog-profile/{id}` | ❌ **TEST-7** | ❌ | ✅ 1 | `ActivityProfileGridTests:200` |
| `DELETE /activity-backlog-profile/{id}` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests:200,684` |
| `POST /activity-backlog-profile/batch-delete` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests.Backlog_BatchDelete_*` |
| `GET /activity-backlog-profile` (GetAll) | ❌ | ❌ | ❌ **TEST-1 🔴** | — |
| `GET /activity-backlog-profile/{id}` | ✅ 2 | ✅ 1 | ✅ 1 | `ActivityProfileGridTests:187,663` |
| `GET /activity-backlog-profile/all-options` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests:233` |
| `POST /activity-backlog-profile/grid` | ✅ 5 | ✅ 3 | ✅ 1 | `ActivityProfileGridTests:170,577,603` |
| `POST /activity-bucket-list-profile` | ✅ 1 | ✅ 1 | ✅ 1 | `ActivityProfileGridTests:338` |
| `PUT /activity-bucket-list-profile/{id}` | ❌ **TEST-7** | ❌ | ✅ 1 | `ActivityProfileGridTests:308` |
| `DELETE /activity-bucket-list-profile/{id}` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests:308,684` |
| `POST /activity-bucket-list-profile/batch-delete` | ❌ | ❌ | ✅ 1 | `ActivityProfileGridTests.BucketList_BatchDelete_WithAForeignId_DeletesNothing` |
| `GET /activity-bucket-list-profile/{id}` | ✅ 2 | — | ✅ 1 | `ActivityProfileGridTests:295,663` |
| `GET /activity-bucket-list-profile/all-options` | ❌ | ❌ | ❌ **TEST-3** | — |
| `POST /activity-bucket-list-profile/grid` | ✅ 4 | ✅ 4 | ✅ 2 | `ActivityProfileGridTests:278,494,521,546,643` |
| `POST /activity-project-profile` | ✅ 1 | ✅ 1 | ✅ 1 | `ActivityProfileGridTests:465` |
| `PUT /activity-project-profile/{id}` | ❌ **TEST-7** | ❌ | ✅ 1 | `ActivityProfileGridTests:391` |
| `PATCH /activity-project-profile/{id}/status` | ✅ 1 | ✅ 1 | ✅ 1 | `ActivityProfileGridTests:424,445` |
| `DELETE /activity-project-profile/{id}` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests:391,684` |
| `POST /activity-project-profile/batch-delete` | ❌ | ❌ | ✅ 1 | `ActivityProfileGridTests.Project_BatchDelete_WithAForeignId_DeletesNothing` |
| `GET /activity-project-profile/{id}` | ✅ 1 | — | ✅ 1 | `ActivityProfileGridTests:378` |
| `GET /activity-project-profile/all-options` | ❌ | ❌ | ❌ **TEST-3** | — |
| `POST /activity-project-profile/grid` | ✅ 1 | ❌ | ✅ 1 | `ActivityProfileGridTests:361` |
| `POST /memory-anchor` | ✅ | ❌ **TEST-4** | ✅ | `ActivityEndpointTests:415` (base) |
| `PUT /memory-anchor/{id}` | ✅ | ✅ | ✅ | `ActivityEndpointTests:450` (base), `MemoryAnchorUpdateRulesTests` |
| `DELETE /memory-anchor/{id}` | ✅ | — | ✅ | `ActivityEndpointTests:471` (base) |
| `POST /memory-anchor/batch-delete` | ✅ | ✅ | ✅ | `ActivityEndpointTests:500` (base) |
| `GET /memory-anchor/{id}` | ✅ | — | ✅ | `ActivityEndpointTests:392` (base) |
| `GET /memory-anchor/all-options` | ✅ | — | ✅ | `ActivityEndpointTests:491` (base) |
| `POST /memory-anchor/filtered-table` | ✅ | ❌ | ✅ | `ActivityEndpointTests:407` (base) |
| lookups ×4 — `GetById`, `Grid`, `Create`, `Update`, `BatchDelete` (20) | ✅ | ✅ | ✅ | `ActivityEndpointTests:519-790` (base) |
| lookups ×4 — `GET /activity-*` (GetAll) (4) | ❌ | ❌ | ❌ **TEST-6** | — |
| `POST /leisure-suggestion` | ✅ 8 | ✅ 5 | ✅ 2 | `LeisureSuggestionTests` |
| `POST /leisure-suggestion/seen` | ✅ 5 | ✅ 3 | ✅ 2 | `LeisureSuggestionTests` |
| `GET /leisure-weather-fit` | ✅ 4 | ✅ 3 | ✅ 2 | `LeisureWeatherFitTests` |

### Services, rules, seeders, seams

| Unit | Tests | File |
|---|---|---|
| `LeisureDrawRanker` (eligibility, score, draw, caps, slot) | 19 | `Services/LeisureDrawRankerTests` |
| `WeatherFitRule` + `WeatherDependencyCodes.Infer` | 13 (5 added with `CQ-10`'s word-boundary fix) | `Services/WeatherFitRuleTests` |
| `ActivityProfileActivityReferenceSource` (merge/repoint) | via `ActivityMergeTests` | `Endpoints/ActivityMergeTests` |
| 4 per-user default seeders | via `PerUserDefaultSeederTests` + `ActivityProfilesSeeders_AreRegisteredExactlyOnce` | `Seeding/`, `ActivityProfilesRouteSmokeTests:71` |
| 8 dev seeders | the 3 profile ones: 12 tests (`TEST-8`, done). The other 5: registration only | `Seeding/DevProfileSeederTests`, `ActivityProfilesRouteSmokeTests:71` |
| `OpenMeteoWeatherProvider` | 13 (`TEST-9`, done 2026-09-04) — closes the *previously known* gap | `Services/OpenMeteoWeatherProviderTests` |
| `LeisureWeather:Enabled` switch | 1 — closed with `TEST-9` | `Services/OpenMeteoWeatherProviderTests` |
| Wiring (routes, single registration, dependency direction) | 5 | `ActivityProfilesRouteSmokeTests` |
| Schema / FK inventory / named filters | owned elsewhere | `ModuleSchemaTests`, `ActivityForeignKeyInventoryTests`, `NamedQueryFilterTests` |

### The honest number

**47 of 57 endpoints (≈82%) have at least one test; ≈65% have an ownership or edge case.** Both pure
rules are near-exhaustively covered and the three leisure routes are the best-tested surface in the
slice.

**The riskiest gaps are all reads and all on the profile families**, in this order: the unscoped
backlog `GetAll` (`TEST-1` / `SEC-1` — every user's rows, today, with nothing red), the three
batch-delete paths whose ownership loop nothing exercises (`TEST-2` — since closed), and the two
select-options routes whose sibling *is* pinned (`TEST-3`). After those, the write-side invariants that exist only
in a create validator and are silently absent from the update path (`TEST-4`, `TEST-5`).

## 12. Missing tests — backlog

Every prompt is self-contained; they share `testingPrompts/_SHARED-CONTEXT.md`.

| ID | Gap | Severity | Prompt |
|---|---|---|---|
| TEST-1 | `GET /activity-backlog-profile` returns every user's profiles — no scoping, no test | 🔴 | [GetAllBacklogProfileScoping.md](testingPrompts/GetAllBacklogProfileScoping.md) |
| ~~TEST-2~~ ✅ done 2026-09-04 | The three profile `batch-delete` routes: ownership + all-or-nothing | 🟠 | [ProfileBatchDelete.md](testingPrompts/ProfileBatchDelete.md) → the four `*_BatchDelete_*` tests in `ActivityProfileGridTests`, written ahead of `CQ-1`'s migration to the bases |
| TEST-3 | `all-options` scoping on bucket-list and project (backlog's is pinned, siblings' are not) | 🟠 | [ProfileSelectOptionsScoping.md](testingPrompts/ProfileSelectOptionsScoping.md) |
| ~~TEST-4~~ ✅ done 2026-09-04 | `PUT /memory-anchor/{id}` repoints `ActivityId` past both create-time rules | 🟠 | [MemoryAnchorUpdateRepoint.md](testingPrompts/MemoryAnchorUpdateRepoint.md) → `MemoryAnchorUpdateRulesTests` |
| TEST-5 | Profile create/update accept another user's lookup ids | 🟠 | [ProfileForeignLookupIds.md](testingPrompts/ProfileForeignLookupIds.md) |
| TEST-6 | The four lookups' `GetAll` routes — registered and user-scoped | 🟡 | [LookupGetAllRoutes.md](testingPrompts/LookupGetAllRoutes.md) |
| TEST-7 | A stale `row_version` on a profile `PUT` answers 400, not 409 | 🟠 | [ProfileUpdateConcurrency.md](testingPrompts/ProfileUpdateConcurrency.md) |
| ~~TEST-8~~ ✅ done 2026-09-04 | Dev profile seeders skip every user after the first | 🟠 | [DevSeederSecondUser.md](testingPrompts/DevSeederSecondUser.md) → `Seeding/DevProfileSeederTests` (12 tests), landed with the `CQ-3` / `CQ-4` fix |
| ~~TEST-9~~ ✅ done 2026-09-04 | A forecast with an empty `daily` block becomes a "dry" badge instead of no signal | 🟠 | [WeatherProviderEmptyDaily.md](testingPrompts/WeatherProviderEmptyDaily.md) → `Services/OpenMeteoWeatherProviderTests` (13 tests), landed with the `CQ-5` fix |

**Previously known, not re-flagged** (all from `docs/testing.md` → *Known gaps*): `MemoryAnchor`,
`LeisureSuggestionRecord` and the four lookups are absent from the user data export and sit on
`UserDataExportAndDeletionTests.KnownGaps`; the two `MemoryAnchor` check constraints are unreachable
through the API; `OpenMeteoWeatherProvider` and the `LeisureWeather:Enabled` switch had no coverage —
**both closed 2026-09-04** by `TEST-9`'s `OpenMeteoWeatherProviderTests`, which went past the one
behaviour the prompt asked for and covers the provider's failure shapes, mapping and cache window;
`CreateMemoryAnchorValidator`'s two business rules are untested — `TEST-4` covers the **update**
half of that, which the doc does not list.
