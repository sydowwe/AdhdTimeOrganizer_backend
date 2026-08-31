# TEST-3 — `POST /activity/merge` refuses an archived merged id

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `SEC-3` 🟠.

**Status today:** `Endpoints/ActivityMergeTests.cs` has 13 tests covering the repoint fan-out, profile
and tracking collisions, cross-role merges, an **archived survivor** (`:369`), unknown survivor,
unknown merged id, empty list, self-merge and cross-user. None of them archives a **merged** id.

## What the code does

`Core/application/endpoint/activity/activity/command/MergeActivityEndpoint.cs:84` vs `:91`

```csharp
var survivor = await dbContext.Set<Activity>()
    .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])          // ← lifted
    .FirstOrDefaultAsync(a => a.Id == req.SurvivorId, ct);

var merged = await dbContext.Set<Activity>()                         // ← NOT lifted
    .Where(a => mergedIds.Contains(a.Id)).ToListAsync(ct);
if (merged.Count != mergedIds.Count) { await Send.NotFoundAsync(ct); return; }
```

An archived merged id is filtered out, so `merged.Count` falls short and the **whole merge** 404s. The
merge dialog draws its candidates from the grid's **All** view — which is the documented reason
`ActivityFilterRequest.IsArchived` is tri-state at all (`GridActivityEndpoint.cs:82-88`) — so archived
duplicates are selectable, and selecting one fails the operation with no indication of which id was the
problem.

The endpoint's own `<remarks>` (`:33-37`) explicitly permit an archived **survivor** and are silent on
the merged side, so the two halves of the same rule disagree.

## Route

`POST api/activity/merge`, body `{ "survivorId": 1, "mergedIds": [2, 3] }`, response
`{ survivorId, mergedCount, repointedCount }` (`MergeActivityResponse`).

## Add to `Endpoints/ActivityMergeTests.cs`

This one belongs in the existing file — it is the same feature, the same helpers
(`ActivityMergeTestSupport.cs`) and the same fixtures. Follow the surrounding style: assert on
surviving rows, not on `repointedCount`.

### 1. An archived activity can be merged away — `SEC-3`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-3: an archived activity may be merged away, not just merged into")]
[Trait("Status", "KnownGap")]
```

Seed an active survivor and an **archived** duplicate carrying at least one referencing row (an
`ActivityHistory` row is the cheapest — `ActivityArchivingTests.cs:316` builds one). Merge.

Assert:
- **200**, `mergedCount == 1`
- the history row now points at the survivor (`.IgnoreQueryFilters()`, assert on the row)
- the archived activity is gone

Today: 404, and nothing moves.

### 2. A mixed set — one active, one archived — merges as a unit

Same shape with `mergedIds: [activeDupe, archivedDupe]`. Assert `mergedCount == 2` and that **both**
sets of referencing rows moved. Today the whole call 404s because of the one archived id, which is the
worst version of the bug: the active duplicate is not merged either, and the user is told "not found"
about a set they can see on screen.

### 3. Archived-into-archived — `SEC-3`, **KnownGap**

Survivor archived **and** merged id archived. Should behave exactly like #1 and leave the survivor
archived. This is the case that proves the fix lifted the filter on both queries rather than swapping
which one is lifted.

### 4. Cross-user is still 404, even when archived — passes today, keep it green

No `KnownGap`. Seed an **archived** activity owned by a second user and try to merge it away from the
default caller. Must stay **404** after the fix — the per-user filter must remain on when the archival
filter is lifted, and confusing the two is the most likely way to break this while fixing it. This is
the regression pin that matters most in the file.

## The fix, for reference

Add `.IgnoreQueryFilters([AppQueryFilters.ActivityArchived])` to the `merged` query on line 91. Nothing
else changes — the repoint/delete path is indifferent to the flag.

## Doc reconciliation

`../../docs/domain-map.md` → Business rules → "Merge is all-or-nothing, and order is load-bearing":
state that archived activities may be merged **in both directions**. Also the class `<remarks>` on
`MergeActivityEndpoint`. See `DOC-4`.
