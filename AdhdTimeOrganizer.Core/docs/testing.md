# AdhdTimeOrganizer.Core — Testing

## How to test this project

Core has **no test project of its own**. Everything that tests it lives in
`AdhdTimeOrganizer.IntegrationTests`, and deliberately so: nearly every rule Core owns is a property of
the *composed* application — the `DbContext` alias, the global query filters, the DI scans, the seam
registrations — and none of them are observable from a library-only unit test.

The infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` + the `"Postgres"`
collection over `AppDbContextFixture` (a real Postgres container), and `UserRoleTestAuthHandler` for
role and ownership scenarios (`CreateUserRoleClient()`, `CreateAdminRoleClient()`). See the portal's
`docs/testing.md` and `docs/claude/testing.md` for the full setup.

## Strategy

**CRUD goes through the Framework base test classes.** `BaseCreateEndpointTests`,
`BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`, `BaseGetAllEndpointTests`,
`BaseGetByIdEndpointTests`, `BaseGetSelectOptionsEndpointTests`, `BaseFilterSortEndpointTests`,
`BaseBatchDeleteEndpointTests` cover the shapes Core's 40 endpoints mostly are. A new endpoint that
subclasses a Framework base should be tested by subclassing the matching base test rather than by
hand.

**Everything Core actually decides is hand-written**, because the interesting behaviour is exactly
what the bases don't know about — archiving, merging, system-key lookup, the seam fan-in.

**Assert on rows, not on routes.** Almost every load-bearing mechanism in Core fails silently: a
missing seam registration lowers a count, a dropped assembly 404s a route, a doubled DI scan runs a
seeder twice. A test that only checks a status code catches none of it. `ActivityMergeTests` asserts on
surviving rows in six projects rather than on the `repointedCount` the endpoint returns — copy that
shape.

## The guards — do not delete these

Each one exists because the failure it catches produces no exception, no log line and no build error.

| Test | Pins |
|---|---|
| `CoreRouteSmokeTests.ActivityAndTimerRoutes_AreRegistered` | Core's assembly is in FastEndpoints' `o.Assemblies`. Missing → every activity and timer route 404s |
| `CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce` | Core is scanned by exactly one of the two DI scans. In both → every `IEnumerable<T>` doubles and each per-user seeder runs twice |
| `SeamWiringTests.*` (6 tests) | Seams are declared in Core and implemented in slice `seam/` folders; both keyed source families cover every key **exactly** once; the single seams and `IActivityReferenceService` resolve exactly once; every `ISeamEvent` has a handler |
| `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` | The merge actually moves rows in every slice — the one assertion a lowered count cannot fake |
| `ActivityForeignKeyInventoryTests.EveryForeignKeyIntoActivity_MatchesTheFrozenInventory` | The full set of activity FKs and their delete behaviours. The merge's repoint-then-delete ordering rests on this inventory, and it has been quietly false before |
| `ActivityArchivingTests.*` | Both halves of archiving: the pickers exclude, everything else still resolves. `Update_DoesNotUnarchive` is the one that catches `ActivityRequest` learning about `IsArchived`; `UsageCount_IsSortable` catches `usageCount` moving out of the SQL projection |
| `ActivityRoleSystemKeyTests.*` | Lookup survives a rename; an unknown key does not fall through to `SystemKey == null` and match every user-created role; a keyed role cannot be deleted; re-seeding does not duplicate a renamed system role |
| `ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles` | The dependency direction, from the other side |
| `UserScopingQueryFilterTests` · `NamedQueryFilterTests` | The per-user filter reads `ScopeUserId` off the context; every `HasQueryFilter` is named, one name per concern |
| `ModuleSchemaTests` · `ModuleWiringTests` | Every entity resolves to a mapped schema (Core maps by **namespace**, not assembly); the module composition holds |
| `TodoListItemDailyRecapTests.TimeIsAttributedByItem_NotByActivity` | `ITodoListItemLoggedTimeSource` keys off `ActivityHistory.TodoListItemId` only. Widening it to "any time on this activity" looks like it can only find *more* and in fact double-counts |
| `DefaultUsersSeederTests` · `PerUserDefaultSeederTests` · `PerUserDefaultMatcherTests` | The seeding chain and the `Collides` matching that keeps re-seeding idempotent |
| `UserDataExportAndDeletionTests` | Every user-scoped entity is either exported or listed in that suite's own `KnownGaps` array — a new Core entity fails it until classified |

Also relevant, though owned elsewhere: `ActivityEndpointTests` (uniqueness invariants, delete
behaviour, clone, quick-edit), `TimerPresetCrudTests` / `TimerPresetValidationTests` /
`TimerPresetCrudAuthMatrixTests`, `CompletionFanOutTests` and `ActivityTimeAutomationTests` (the seam
events, asserted on rows).

## Known gaps (living list)

Nothing in Core is currently carried as an expected-failing `[Trait("Status","KnownGap")]` test — the
items that used to be (the `DefaultUsersSeeder` create-path failure, `Activity.Clone`'s
`MemberwiseClone`) are now green regression pins rather than gaps.

Two standing caveats rather than gaps:

- **`UserDataExportAndDeletionTests.KnownGaps`** is a real, deliberately maintained list of user-scoped
  entities that are not exported. Adding a user-scoped entity to Core fails that test until it is
  classified one way or the other — that is the intended behaviour, not a flake.
- **Auditing exists in Framework but is not wired up in this app.** Nothing writes audit rows today, so
  no test here asserts on them and the `[AuditIgnore]` attributes on `User` and `Activity.Text` are
  currently inert declarations of intent.
