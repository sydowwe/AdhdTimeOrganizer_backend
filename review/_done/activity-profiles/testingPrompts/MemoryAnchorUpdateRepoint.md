# TEST-4 — `PUT /api/memory-anchor/{id}` must not repoint the anchor onto another activity

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **SEC-3 🟠**, and covers the *update* half of the gap `docs/testing.md` already lists for
`CreateMemoryAnchorValidator`'s two business rules.

## The endpoint

- Route: `PUT /api/memory-anchor/{id:long}` →
  `application/endpoint/memoryAnchor/command/UpdateMemoryAnchorEndpoint.cs`, a
  `BaseUpdateEndpoint<MemoryAnchor, MemoryAnchorRequest>` with
  `Validator<UpdateMemoryAnchorValidator>()`.
- Request (`application/dto/request/MemoryAnchorRequest.cs`):

```json
{ "activityId": 7, "anchorMonth": 5, "anchorYear": 2026, "highlightNote": "…", "rating": 8 }
```

- Response: **200 with the entity id** (`BaseUpdateEndpoint:100`), not 204.
- `UpdateEntity` (`MemoryAnchorRequest.cs:16-23`) writes `e.ActivityId = ActivityId;`.
  `UpdateMemoryAnchorValidator.cs:9-15` checks month/year/rating/note only.
- The row itself is loaded through `FindAsync` on the filtered `DbSet`
  (`BaseUpdateEndpoint.LoadAsync:118`) and `MemoryAnchor` **is** `IEntityWithUser`, so a foreign
  anchor id is already a 404. The hole is the `activityId` in the **body**.

## Scenarios

1. **A foreign `activityId` is refused.** Seed the caller's own anchor (on an activity that has a
   backlog profile). Seed a second user with their own activity. `PUT` the caller's anchor with
   `activityId` = the second user's activity id. Expect a **400** (validation) and — the assertion
   that matters — the row's `ActivityId` **unchanged** in a fresh context.
2. **A non-existent `activityId` is refused**, with the same row-unchanged assertion. Today this
   throws on the FK and returns 400 through `DbUtils.HandleException`, so a status-code-only test
   passes while the *foreign* case (scenario 1) succeeds with a 200. Assert the row.
3. **The caller's own other activity, with no profile, is refused.** `CreateMemoryAnchorValidator`
   (`:49-51`) requires a backlog or bucket-list profile before an activity can be anchored; the
   update path enforces nothing. Seed a bare activity for the caller and repoint onto it.
4. **The two-per-month ceiling survives an update.** Seed two anchors for activity A in 2026-05 and
   one for activity B in the same month; `PUT` B's anchor onto A. `CreateMemoryAnchorValidator:52-54`
   would refuse the equivalent create; the update must too. (If the fix is "make `ActivityId`
   immutable", this scenario becomes redundant — say so in the `DisplayName` and keep it as the pin
   on the year/month edit path instead: change *only* `anchorMonth` onto a month that already has
   two.)
5. **A legitimate edit still works.** Same `activityId`, new `highlightNote` and `rating` → 200 and
   the new values in a fresh context. This is the test that stops the fix from being "reject
   everything".

## The fix (preferred)

Delete `e.ActivityId = ActivityId;` from `MemoryAnchorRequest.UpdateEntity`. Which activity an anchor
is about is identity, not an editable field, and the three profile request DTOs already leave
`ActivityId` alone in `UpdateEntity` for exactly this reason
(`ActivityBacklogProfileRequest.cs:34-45`). Then scenario 1 asserts a **200 with the row unchanged**
rather than a 400 — pick one and state it in the `DisplayName`; silently accepting a field you ignore
is defensible for a PUT of the full resource, but it must be written down.

If instead `ActivityId` stays editable, `UpdateMemoryAnchorValidator` needs the whole `CustomAsync`
projection from `CreateMemoryAnchorValidator:20-55`, with the `ExistingCount >= 2` check excluding
the row being edited.
