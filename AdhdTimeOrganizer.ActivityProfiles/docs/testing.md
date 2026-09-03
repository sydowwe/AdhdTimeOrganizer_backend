# AdhdTimeOrganizer.ActivityProfiles — Testing

## How to test this project

ActivityProfiles has **no test project of its own**. Everything lives in
`AdhdTimeOrganizer.IntegrationTests`, split by what the test actually needs:

| File | Needs | What's there |
|---|---|---|
| `Services/LeisureDrawRankerTests` | Nothing — plain xUnit | The whole ranking rule against hand-built `LeisureCandidate`s: what is excluded rather than penalised, seed reproducibility, row-order independence, the recency and effort-contrast signals, the source caps and the slot arithmetic. |
| `Services/WeatherFitRuleTests` | Nothing — plain xUnit | Which of the four `WeatherDependencyCodes` a day fits, and the label-inference fallback for a row the user invented. |
| `Endpoints/ActivityProfileGridTests` | Postgres | The three profile grids' hand-written user scoping, the derived-completion rule (`IsAnchored` / `MemoryAnchorId`) on the bucket-list and backlog surfaces, the tri-state filter, sorting on the derived field, the status patch, and the activity-delete cascade. |
| `Endpoints/LeisureSuggestionTests` | Postgres | The draw and the `seen` upsert end to end: card shape per source, both empty states, ownership on both routes, the 90-day sweep, the cascade, validation, and that rejecting a draw changes the next one at the same seed. |
| `Endpoints/LeisureWeatherFitTests` | Postgres | The weather route: code-matching survives a rename, the never-throws contract in all three failure shapes, per-user scoping, and the location round-tripping through the preferences endpoint. |
| `Endpoints/ActivityProfilesRouteSmokeTests` | Postgres | Wiring: one route per endpoint family, single registration of the 12 seeders and of the weather provider, and the Core-must-not-reference-this assertion. |
| `Endpoints/ActivityEndpointTests` | Postgres | The lookup and memory-anchor CRUD, through the Framework base test classes (see below). |

Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` over the `"Postgres"`
collection (`AppDbContextFixture`, a real container), and `UserRoleTestAuthHandler` for role and
ownership scenarios. See the portal's `docs/testing.md` and `docs/claude/testing.md`.

Two bespoke helpers you will want:

- **The two weather doubles in `LeisureWeatherFitTests`** — `StubWeatherProvider` (returns a fixed
  `DailyWeather`, or null) and `ThrowingWeatherProvider`. Both are injected through
  `CreateFactory(..., configureServices:)`, which is the only way to test this feature: the real
  provider makes an outbound call and no test may depend on today's weather in a real place.
  `ThrowingWeatherProvider` exists specifically because the endpoint carries its own `catch` **on top
  of** the provider's never-throw promise, and a belt-and-braces guard nobody tests is not a guard.
- **The seeding helpers in `ActivityProfileGridTests`** (`SeedMemoryAnchorAsync`,
  `SeedBacklogAsync` and friends) — use these rather than hand-building rows. `MemoryAnchor` has two
  check constraints and a `(ActivityId, AnchorYear, AnchorMonth)` index, and a hand-built row usually
  trips one of them for reasons unrelated to the test.

## Strategy

**Both pure rules are unit-tested; anything touching scoping, the schema or the wiring is
integration-tested.** `LeisureDrawRanker` and `WeatherFitRule` are static, take their clock and seed as
parameters and persist nothing, so every branch — a weight, a threshold, a cap, an exclusion — is a
cheap unit test. Don't push those into Postgres.

**Lookup and anchor CRUD goes through the Framework base test classes.** The four lookups and
`MemoryAnchor` each have a `Base{GetById,Grid,Create,Update,Delete,BatchDelete,GetSelectOptions}EndpointTests`
subclass in `ActivityEndpointTests.cs`; a new lookup gets the same treatment rather than hand-written
tests. The three **profiles** have no base-class subclasses — their bases can't know about the
hand-written scoping or the derived completion, so `ActivityProfileGridTests` covers them by hand.

**Assert on rows, not on routes.** Everything load-bearing here fails silently: a deleted
`ApplyUserScoping` override still answers 200, a lost `HasConstraintName` still migrates, a lifetime
marker on the weather provider still boots, and a broken weather signal is indistinguishable from
"nothing matched today". None of it changes a status code — which is why the weather tests assert on
the resolved **id set** and the scoping tests always seed a second user.

**A second user is the whole point of the scoping tests.** The failure — a grid that returns
everyone's profiles — is invisible in a single-user test, which is what you write by default.

## The guards — do not delete these

| Test | Pins |
|---|---|
| `ActivityProfileGridTests.{Backlog,BucketList,Project}_Grid_OnlyReturnsCallersOwnRows` | The hand-written `ApplyUserScoping` on the three profile grids. `ApplyUserScoping` is a no-op virtual and the profiles are not `IEntityWithUser`, so deleting an override leaks every user's profiles with nothing going red |
| `ActivityProfileGridTests.Backlog_GetAll_OnlyReturnsCallersOwnRows` | The `Filter` override on `GetAllActivityBacklogProfileEndpoint`. `BaseGetAllEndpoint` has no `ApplyUserScoping` hook and its `Filter` is the identity function, so an empty subclass body reads the whole table and still answers 200 — which is what this route did |
| `NamedQueryFilterTests.ActivityProfiles_CarryBothTheMirroredUserScopeAndArchivedFilters` · `…ProfileUserScopeFilter_HidesForeignProfiles_AndKeepsTheUnscopedEscape` | The global floor under all of the above: `AppQueryFilters.ActivityProfileUserScope` on the three profiles, that it is **named** (unnamed it would replace the archival filter these three already carry), and that its `ScopeUserId == null` escape still lets the seeders / merge seam / export read every row |
| `*_GetById_ForeignProfile_Returns404` · `*_UpdateAndDelete_ForeignProfile_AreRefusedAndRowUnchanged` · `Project_PatchStatus_ForeignProfile_IsRefusedAndRowUnchanged` | The same scoping on the single-row paths, which each check ownership through `p.Activity.UserId` in their own copy of the code |
| `BucketList_Grid_ReportsAnchoredAndTheLatestAnchorId` · `Backlog_Grid_RepeatableEntry_IsNeverAnchored` · `BucketList_GetById_ReportsTheSameCompletionAsTheGrid` | The derived "done" rule and its three separate copies (grid projection, filter predicate, GetById overlay) saying the same thing. Nothing in the schema records completion, so drift here costs no migration and throws nothing |
| `BucketList_Grid_SortsByIsAnchored` | That completion is computed **in the projection**. Overlaid after the query it would sort on `false` for every row and the page would come back in an arbitrary order, with no error |
| `BucketList_Grid_AnotherUsersAnchorDoesNotMarkTheEntryDone` | The anchor set the projection is handed is scoped too — otherwise a stranger's anchor completes your bucket list |
| `*_SecondProfileForSameActivity_IsRejected` (×3) | One profile per activity, enforced by the create validators' subqueries. There is no unique index behind it |
| `DeletingActivity_CascadesToItsProfile_NoOrphanSurvives` | The four `Activity` FKs still cascade after the navigations were deleted and the constraint names pinned |
| `LeisureDrawRankerTests.TheSameSeedOverTheSamePool_DrawsTheSameCardsInTheSameOrder` · `TheDrawDoesNotDependOnTheOrderTheRowsArrivedIn` | The determinism contract. The seed lives in the picker's URL, so a reload must reproduce the draw; anything that makes order depend on row order breaks a promise no exception reports |
| `ThinPool_RelaxesTheCapsRatherThanReturningFewerCards` | The source caps never cost the user a card — the failure is a short draw that looks like "you have nothing filed" |
| `AProjectThatNeedsShopping_IsNeverOffered` (and its endpoint twin) | Readiness is a hard constraint, not a penalty |
| `LeisureSuggestionTests.RenderingADraw_RecordsNothingByItself` | Only `/seen` writes history. Recording on render would demote the cards on screen and stop a seeded URL reproducing its own draw |
| `RecordingTheSameKeyTwice_MovesTheRowRatherThanAddingOne` · `TheSameActivityInTwoSources_KeepsTwoIndependentHistories` | The `(user_id, source, activity_id)` upsert key, in both directions |
| `AnOldRecord_IsSweptOnTheNextWrite` | The 90-day sweep. There is no scheduled job — the write path is the only thing that forgets |
| `AnotherUsersProfiles_AreNeverDrawn` · `AKeyNamingAnotherUsersActivity_RecordsNothing` | Ownership on both leisure routes, which are hand-written and sit on no CRUD base |
| `LeisureWeatherFitTests.ARenamedRow_StillMatchesOnItsCode` | Matching is on `Code`, not `Text`. The lookup is per-user and editable, so label matching breaks on the first rename and never works in a second locale |
| `ARowTheUserInvented_MatchesOnWhatItsLabelSays` | The `WeatherDependencyCodes.Infer` fallback, which is deliberately *not* persisted |
| `WithNoLocationSet_...` · `WhenTheProviderHasNothing_...` · `AProviderThatThrows_StillAnswers200WithNoSignal` | The never-fail contract: every failure shape is "no weather opinion", never an error and never an exclusion |
| `ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles` | The direction of the dependency. Re-adding one of the deleted `Activity` / `User` navigations compiles fine and inverts the edge every slice depends on |
| `ActivityProfilesSeeders_AreRegisteredExactlyOnce` | This assembly is in `ModuleAssemblies` **and** reachable by the `AppDomain` sweep — double registration makes the dev seeders truncate and reseed twice and the per-user defaults double-insert |
| `TheWeatherProvider_IsRegisteredExactlyOnce` | The typed `HttpClient` must stay marker-free. A lifetime marker "to help" registers it twice |
| `ProfileRoutes_AreRegistered` · `LookupAndAnchorGridRoutes_AreRegistered` | FastEndpoints `o.Assemblies` in `Program.cs`. Missing, every route in this project 404s and nothing else notices |

Owned elsewhere but load-bearing for this project: `ModuleSchemaTests` (this assembly resolves to the
`activity_profiles` schema — the resolver throws for anything unmapped, so a new entity here is
covered too), `ActivityForeignKeyInventoryTests` and `ActivityMergeTests` (the `Activity` FK family,
including the four pinned constraint names), `NamedQueryFilterTests`, `PerUserDefaultSeederTests` /
`PerUserDefaultMatcherTests` (the four lookup defaults and their `(user_id, text)` collision key),
`AccountDeletionSummaryTests` and `UserDataExportAndDeletionTests` (see below).

## Known gaps (living list)

Nothing here is carried as an expected-failing `[Trait("Status","KnownGap")]` test.

Real gaps, in decision terms:

- **`MemoryAnchor` and `LeisureSuggestionRecord` are not in the user data export**, and neither are the
  four lookups. All six are on `UserDataExportAndDeletionTests.KnownGaps` — the anchors as
  user-authored content that plausibly belongs in an Art. 15 export, the lookups as reference data the
  exported rows name by id. Adding a new user-scoped entity to this project fails that test until it
  is classified one way or the other — intended, not a flake. Erasure is unaffected: everything here
  cascades from `User` or `Activity`.
- **`CreateMemoryAnchorValidator`'s two business rules are untested** — "at most 2 anchors per activity
  per month" and "the activity must have a backlog or bucket-list profile to be anchored". The base
  `MemoryAnchorCreateTests` only exercises the happy path, so both rules could be dropped in a refactor
  of the subquery projection without a failure.
- **The two `MemoryAnchor` check constraints are never reached through the API**, because the validator
  rejects the same values first. Nothing proves the DB would still refuse a month of 13 if the
  validator changed shape.
- **`GetAllActivityBacklogProfileEndpoint`'s projection has no test.** Its scoping is now pinned by
  `Backlog_GetAll_OnlyReturnsCallersOwnRows`, but it is the one profile endpoint using the plain
  `Projection`, which reports "not done" for `IsAnchored` / `MemoryAnchorId` by design — and nothing
  records that expectation, so a caller adopting it will quietly get the wrong completion.
- **`OpenMeteoWeatherProvider` itself is untested.** Every weather test stubs `IDailyWeatherProvider`,
  so the geocoding call, the response mapping, the two cache entries and the next-local-midnight expiry
  cap have no coverage at all. A provider that silently returns null for every real location looks
  exactly like a user with no location set.
- **Nothing asserts the `LeisureWeather:Enabled` switch actually suppresses the outbound calls** — only
  that a provider with nothing to say produces an empty set.
