# TEST-4 — the reference seam runs under the archival filter it never lifts

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `SEC-4` 🟠.

**Status today:** `ActivityArchivingTests:353` (`UsageCount_CountsEveryReferencingSlice_AndCanDeleteFollowsIt`)
and `:386` (`GetById_CarriesUsageCountAndCanDelete`) both count references on **active** activities.
`ActivityMergeTests:369` merges into an archived survivor that carries **no profile**. The intersection
— archived activity × filtered dependent — is untested, and it is where the bug lives.

## What the code does

Three of the entities the reference seam reads carry a **second** ambient filter that nothing lifts:

- `AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs:296-311` mirrors
  `AppQueryFilters.ActivityArchived` onto `ActivityBacklogProfile`, `ActivityBucketListProfile`,
  `ActivityProjectProfile` (and the two `PlannerSuggestionFrom*` views) as `p => !p.Activity.IsArchived`
- `AdhdTimeOrganizer.ActivityProfiles/application/seam/ActivityProfileActivityReferenceSource.cs:50-55`
  reads all three with no lift
- `Core/application/seam/IActivityReferenceSource.cs:62-67` — the contract's remarks address **only**
  user scoping and state that "no source here needs a hand-written scope"
- `Core/application/service/ActivityReferenceService.cs:57-73` (`CountByActivityAsync`) and
  `GetByIdActivityEndpoint.cs:35-41` compose that into a **separate** query that lifts nothing

So an archived activity's profile rows are invisible to the count, and `canDelete = usageCount == 0`
can come back `true` while three profile rows exist. `DeleteActivityEndpoint.cs:16` deliberately lifts
the archival filter so the archive view's delete row action works — so the UI offers the button, the
button succeeds, and the profiles cascade away.

## Create `Endpoints/ArchivedActivityReferenceCountTests.cs`

`[Collection("Postgres")]` + `PostgresTestBase`. You need one `ActivityBacklogProfile` — copy its
construction from `Endpoints/ActivityMergeTests.cs:229`
(`Merge_WhenBothCarryAProfile_TheSurvivorsProfileWins`) or `ActivityMergeTestSupport.cs`, which already
builds profiles for the merge tests.

### 1. `GET /activity/{id}` on an archived activity counts its profile — `SEC-4`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-4: usageCount on an archived activity still counts its profile rows")]
[Trait("Status", "KnownGap")]
```

Seed an activity, give it an `ActivityBacklogProfile`, then archive it
(`PATCH api/activity/{id}/archived` with `{ IsArchived = true }` — use the endpoint, not a direct DB
write, so the test exercises the real state transition).

`GET api/activity/{id}` — the route already returns archived activities normally
(`GetByIdActivityEndpoint.cs:24`).

Assert: `usageCount >= 1` and `canDelete == false`.

Today: `usageCount == 0`, `canDelete == true`.

### 2. The `canDelete: true` is actually destructive — `SEC-4`, **KnownGap**

The consequence, spelled out so the fix cannot be argued down to "a cosmetic count":

1. same setup as #1; confirm the response says `canDelete: true`
2. `DELETE api/activity/{id}` → 204 (this route lifts the archival filter deliberately)
3. fresh context, `.IgnoreQueryFilters()`: the `ActivityBacklogProfile` row **is gone**

After the fix, step 1's assertion flips and the test becomes "the UI is not offered a delete that
would destroy a profile". Rewrite the assertion, keep the file.

### 3. Merging into an archived survivor that already holds a profile — `SEC-4`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-4: merging into an archived survivor that already has a profile does not violate its unique index")]
[Trait("Status", "KnownGap")]
```

This is the sharpest case. `ActivityId` is **unique** on all three profile tables.
`ActivityProfileActivityReferenceSource.RepointUniqueProfileAsync:90` decides whether the survivor
already has a profile with `AnyAsync(p => p.ActivityId == survivorId)` — under the archival filter,
which hides the archived survivor's own profile. So `survivorHasOne` is `false`, the merged profile is
repointed onto an activity that already has one, the unique index is violated and the entire
transaction rolls back.

Setup: survivor **archived** and carrying an `ActivityBacklogProfile`; one active duplicate also
carrying one. `POST api/activity/merge`.

Assert: **200**; exactly **one** `ActivityBacklogProfile` row exists for the survivor
(`.IgnoreQueryFilters()`), and it is the **survivor's own** (compare on `Id` — the survivor's profile
wins per the documented collision rule); the duplicate's profile row is gone; the duplicate activity is
gone.

Today: a 400/500 and a rolled-back merge, on an irreversible action the user just confirmed.

### 4. Active activities are unaffected — passes today, keep it green

No `KnownGap`. The same three scenarios with nothing archived. This is the regression pin that stops a
fix from double-counting: lifting the archival filter inside the seam must not change any count on an
active activity, because the filter was never hiding anything there.

### 5. Verify the grid path rather than assuming it — passes today (probably)

The grid's **All** view lifts the named filter on the outer query
(`GridActivityEndpoint.cs:84-85`), and EF applies a named `IgnoreQueryFilters` across the whole
compiled tree including correlated subqueries — so the grid is probably already correct while
`GET /activity/{id}` is not. Nothing asserts it. Add:

```csharp
[Fact(DisplayName = "SEC-4: the All grid view counts an archived activity's profiles")]
```

`POST api/activity/filtered-table` with `useFilter: true, filter: { isArchived: null }` and assert the
archived activity's row carries `usageCount >= 1`. If it passes, you have documented the asymmetry with
#1 — which is itself the evidence that the count needs to be lifted in one central place rather than
left to whichever caller happens to lift it.

## The fix, for reference

Decide the rule in Core and write it into `IActivityReferenceSource`'s contract: **references are
counted regardless of archival state** — the only rule consistent with what `canDelete` promises. Then
lift `AppQueryFilters.ActivityArchived` centrally in `ActivityReferenceService.ReferencingActivityIds`
and `CountByActivityAsync` (one place, all seven sources) rather than in each implementation. The
`survivorHasOne` check in `ActivityProfileActivityReferenceSource` must lift it either way.

## Doc reconciliation

`../../AdhdTimeOrganizer.Core/application/seam/README.md` → "How each one fails": add the
archival-filter mode to `IActivityReferenceSource`. `../../docs/domain-map.md` → Invariants #7: a
seam's `IQueryable` must stay composable **and** be explicit about which ambient filters it runs
under. See `DOC-3`.
