# AdhdTimeOrganizer.Core — 02 · Findings

Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker (legal / security / data-corruption) ·
🟠 important (wrong behaviour or maintainability hole) · 🟡 polish.
Every finding carries a `Doc impact:` line naming what a fix must reconcile in `../docs/`.

**Previously known, not re-flagged:** the three nullable preset cascades
(`PomodoroTimerPreset.Focus/RestActivityId`, `TimerPreset.ActivityId` → `Cascade` rather than
`SetNull`) are carried as an explicit `TODO` / "⚠ OPEN QUESTION" block in
`AdhdTimeOrganizer.IntegrationTests/Modules/ActivityForeignKeyInventoryTests.cs:72-84`. It is a live
open decision, not a new finding. It resurfaces only as a rollout risk in
[03-risks-rollout.md](03-risks-rollout.md) (`MIG-3`) — and note that `SEC-1` below is the *same
question asked one level up*, where the answer is much less defensible.

---

## 5. Security & authorization

### `SEC-1` 🔴 ✅ FIXED 2026-09-01 · Deleting an activity role or category destroys every activity under it — and everything in every slice that pointed at those activities

> **Resolved.** All four parts of the suggested fix landed together: `CategoryId` → `SetNull`
> (migration `20260901180030_ActivityCategorySetNullOnDelete`, FK swap only, no data change);
> `DeleteActivityRoleEndpoint` counts dependent activities with `ActivityArchived` lifted and answers
> 409 beside its `SystemKey` refusal; `usageCount` / `canDelete` project onto `ActivityRoleResponse`
> and `ActivityCategoryResponse` on both lookups' grid and get-by-id; and
> `ActivityForeignKeyInventoryTests` gained a second frozen inventory for the FKs *out of* `Activity`.
> Behavioural guard: `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityLookupDeleteCascadeTests.cs`
> (7 tests, no `KnownGap` traits). Docs reconciled in `docs/domain-map.md` (invariants 3 and 5, new
> business rule "Deleting a role or a category") and `docs/summary.md` (Gotchas). The description below
> is the original finding, kept as written.

`AdhdTimeOrganizer.Core/infrastructure/persistence/configuration/activity/ActivityConfiguration.cs:25-34`

```csharp
builder.HasOne(e => e.Role).WithMany(r => r.Activities)
    .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade).IsRequired();

builder.HasOne(e => e.Category).WithMany(r => r.Activities)
    .HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);   // ← optional FK, cascading
```

`DELETE /activity-role/{id}` (`role/command/DeleteActivityRoleEndpoint.cs:15`),
`DELETE /activity-category/{id}` (`category/command/DeleteActivityCategoryEndpoint.cs:7`) and
`POST /activity-category/batch-delete` (`category/command/BatchDeleteActivityCategoryEndpoint.cs:7`)
are plain `BaseDeleteEndpoint` / `BaseBatchDeleteEndpoint` subclasses. They issue a `DELETE` on the
lookup row and return 204. Postgres then cascades to every `Activity` carrying that `role_id` /
`category_id`, and each of those activities cascades again through all 17 real activity FKs — history
rows, planner tasks (all three shapes), to-do items, routine to-do lists, tracker mappings, all three
profiles, memory anchors, leisure suggestion records, timer presets.

Why it matters, concretely: a user tidying up Settings → Categories deletes "Deep Work" (which they
never used deliberately, it came from a seeder) and silently loses every activity filed under it plus
a year of that activity's history. The response is `204 No Content`. There is no confirmation
payload, no `usageCount`, no `canDelete`, no 409, no log line. The whole `usageCount` / `canDelete`
machinery this module built for `Activity` — a correlated count over twelve tables, a sortable grid
column, a gated delete button, an entire seam — exists **only one level down** and is absent on the
two lookups that can destroy the same rows in bulk.

The category half is the worse one and is almost certainly not a decision anybody made: the FK is
**optional** (`long? CategoryId`), so `DeleteBehavior.SetNull` is available and is the obvious
semantics — losing a category should blank the classification, not destroy the thing classified. The
solution already uses exactly that shape for the structurally identical
`TodoListItem.PairedLeisureActivityId`. The role half is a genuine product question (a required FK
cannot go null), but the answer is `Restrict` + a 409 the UI can explain, or a reassign-then-delete
flow — not a silent cascade.

`ActivityForeignKeyInventoryTests` cannot catch this: it freezes FKs whose **principal** is
`Activity`. These two have `Activity` as the *dependent*, so they are outside its query and always
have been.

**Suggested fix.** (1) Change `CategoryId` to `DeleteBehavior.SetNull` (migration; no data change).
(2) Give `DeleteActivityRoleEndpoint` the same shape as its existing system-key refusal — count
dependent activities first and answer 409 with the count when non-zero, alongside the 409 it already
raises for `SystemKey`. (3) Project `usageCount` / `canDelete` onto `ActivityRoleResponse` and
`ActivityCategoryResponse` the way `ActivityResponse` has them, so the UI can gate the button. (4)
Extend `ActivityForeignKeyInventoryTests` with a second frozen inventory of FKs *out of* `Activity`.

**Doc impact:** `domain-map.md` → Invariants #3 (state the blast radius, not just "both cascade"); new
Business rule "Deleting a role or a category" beside "Archiving vs. deleting an activity";
`summary.md` → Gotchas (the `usageCount` promise stops at `Activity`).

---

### `SEC-2` 🟠 ✅ FIXED 2026-09-01 · Every Core write accepts another user's foreign-key ids

> **Resolved.** `infrastructure/persistence/extensions/CallerVisibleReferenceExtensions.cs` looks a
> caller-supplied id up through the caller's own filtered `DbSet` (`AllVisibleAsync<T>`, plus
> `AllActivitiesVisibleAsync` which lifts `ActivityArchived` so the check is about ownership only and
> an existing row pointing at a since-archived activity stays editable). It is wired into the
> `AfterMapping` hook of `CreateActivityEndpoint`, `UpdateActivityEndpoint`, `Create`/`UpdateTimerPresetEndpoint`
> and `Create`/`UpdatePomodoroTimerPresetEndpoint`, and inline in the two hand-rolled
> `QuickEditActivityEndpoint` / `CloneActivityEndpoint` before anything is mutated. All of them answer
> **404**, matching `MergeActivityEndpoint`. Behavioural guard:
> `AdhdTimeOrganizer.IntegrationTests/Endpoints/CoreForeignKeyOwnershipTests.cs` (8 tests, no
> `KnownGap` traits), including the cross-user cascade chain end to end. Docs reconciled in
> `docs/domain-map.md` (invariant 6) and, per the note below, the identical wording in
> `AdhdTimeOrganizer.TodoLists/docs/domain-map.md` (invariant 3) — TodoLists' own `SEC-1` is still
> open and the doc now says so. The description below is the original finding, kept as written.

- `application/dto/request/activity/ActivityRequest.cs:12-24` — `RoleId`, `CategoryId` copied straight
  into the entity by `ToEntity` / `UpdateEntity`
- `application/dto/request/activity/QuickEditActivityRequest.cs:7` → `QuickEditActivityEndpoint.cs:41`
- `application/dto/request/timer/TimerPresetRequest.cs:10,12,17` — `ActivityId`
- `application/dto/request/timer/PomodoroTimerPresetRequest.cs:28-30,41-42,53-54` — both columns
- Validators check magnitude only: `ActivityValidator.cs:18-19` (`RoleId` `NotEmpty`),
  `QuickEditActivityValidator.cs:13`, `TimerPresetValidator.cs:14-16`,
  `PomodoroTimerPresetValidator.cs:30-36` (`GreaterThan(0)`)

The per-user global query filter scopes **reads** of `IEntityWithUser`. This is a **write** of a raw
`long` into an FK column whose target row genuinely exists in the database, so the constraint is
satisfied and nothing rejects it. User A can `POST /timer-preset` with `activityId` = one of user B's
activities, or `POST /activity` with `roleId` = one of B's roles.

Three distinct consequences, in increasing order of how much they matter:

1. **Existence oracle.** A valid id returns 201; an invalid one returns a FK-violation 400. That
   makes another account's activity/role ids enumerable — which is exactly what
   `MergeActivityEndpoint`'s "404, never 403" comment (`:81-83`) goes out of its way to prevent on the
   read side.
2. **Rows that vanish.** `Activity.RoleId` is required and the grid projects `e.Role.Name`
   (`GridActivityEndpoint.cs:75`). With a foreign `roleId` the required-navigation join is against a
   user-filtered `ActivityRole` set, so the row drops out of the caller's own grid entirely — an
   activity that exists, is theirs, and is unreachable through the UI.
3. **Cross-user destruction.** A's preset points at B's activity with `DeleteBehavior.Cascade`. When
   **B** deletes that activity, **A's** preset row is destroyed — and B's `usageCount` read that
   activity as 0 (A's preset is invisible behind B's user filter), so `canDelete` promised it was
   safe. `canDelete` is documented as "a promise the UI renders as an enabled delete button"
   (`ActivityResponse.cs:31-54`); this is that promise being wrong through no fault of B's.

This is the same defect class the TodoLists review logged as its `SEC-1`
(`review/todo-lists/testingPrompts/CreateTodoListItemForeignKeyOwnership.md`), and
`MoveTodoListItemEndpoint.cs:51-54` already shows the intended shape: verify the referenced row is
visible to the caller and answer 404 when it is not.

**Suggested fix.** Add a shared `AfterMapping`/validator-side ownership check for activity-scoped FKs
in Core, and use it from `CreateActivityEndpoint`, `UpdateActivityEndpoint`,
`QuickEditActivityEndpoint`, `CloneActivityEndpoint` and the four preset endpoints. 404 (not 403), so
existence stays unconfirmable across accounts, matching `MergeActivityEndpoint`.

**Doc impact:** `domain-map.md` → Invariants #6 — the sentence "Per-user scoping is the DbContext's
job … not the endpoints" currently reads as a licence to skip this. It needs the qualifier that it
governs **reads only**, and that any caller-supplied FK id is an endpoint responsibility. (Identical
wording is flagged as `DOC-5` in the TodoLists review; fix both or neither.)

---

### `SEC-3` 🟠 `POST /activity/merge` refuses any merged id that is archived, while the dialog offers exactly those

`application/endpoint/activity/activity/command/MergeActivityEndpoint.cs:84` vs `:91`

```csharp
var survivor = await dbContext.Set<Activity>()
    .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])           // ← lifted
    .FirstOrDefaultAsync(a => a.Id == req.SurvivorId, ct);

var merged = await dbContext.Set<Activity>()                          // ← NOT lifted
    .Where(a => mergedIds.Contains(a.Id)).ToListAsync(ct);
if (merged.Count != mergedIds.Count) { await Send.NotFoundAsync(ct); return; }
```

The archival filter is lifted for the survivor and not for the merged set. The merge dialog draws its
candidates from the grid's **All** view — which the docs describe as the reason `IsArchived` is
tri-state at all (`domain-map.md`, "The unfiltered grid view means active only"; `GridActivityEndpoint.cs:82-88`)
— so an archived duplicate is selectable, and selecting one 404s the whole merge with no indication
of which id was the problem. Deduplicating an activity you already archived is a completely ordinary
thing to want, and it is the single case the feature cannot do.

The asymmetry also makes the endpoint's own documented rules incoherent: an archived **survivor** is
explicitly allowed (`:33-37`), an archived **merged id** is silently impossible, and the class
docstring does not mention the second.

**Suggested fix.** Lift `AppQueryFilters.ActivityArchived` on the `merged` query too. The per-user
filter stays on either way, so cross-user remains 404. No other change is needed — the repoint/delete
path is indifferent to the flag.

**Doc impact:** `domain-map.md` → Business rules → "Merge is all-or-nothing" (add: archived
activities may be merged away as well as merged into); `MergeActivityEndpoint`'s own `<remarks>`.

---

### `SEC-4` 🟠 The reference seam ignores the archival query filter, so an archived activity can report `usageCount: 0` while its profiles exist

- `application/seam/IActivityReferenceSource.cs:62-67` — the contract's remarks address **only** user
  scoping: *"No user scoping here on purpose: `AppDbContext`'s global `IEntityWithUser` filter already
  scopes every entity involved… so no source here needs a hand-written scope."*
- `AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs:296-311` — but there is a **second**
  ambient filter: `ActivityArchived` is mirrored onto `ActivityBacklogProfile`,
  `ActivityBucketListProfile`, `ActivityProjectProfile` and the two suggestion views as
  `p => !p.Activity.IsArchived`
- `AdhdTimeOrganizer.ActivityProfiles/application/seam/ActivityProfileActivityReferenceSource.cs:50-55`
  reads all three profile sets with no lift
- `application/service/ActivityReferenceService.cs:57-73` (`CountByActivityAsync`) and
  `GetByIdActivityEndpoint.cs:35-41` compose that into a **separate** query that never lifts anything

Two concrete failures:

1. **`GET /activity/{id}` on an archived activity under-counts.** The activity is archived, so its
   three profile rows are hidden from the source's subquery, so `usageCount` omits them and
   `CanDelete = usageCount == 0` can come back `true`. `DeleteActivityEndpoint` deliberately lifts the
   archival filter (`:16`) precisely so the archive view's delete row action works — so the UI offers
   the button, the button succeeds, and the profiles cascade away. That is the exact broken promise
   `canDelete` exists to prevent, arriving through the one code path that lifts the filter for the
   delete but not for the count.
2. **Merging into an archived survivor that already holds a profile fails the whole merge.**
   `RepointUniqueProfileAsync` (`ActivityProfileActivityReferenceSource.cs:90`) asks
   `AnyAsync(p => p.ActivityId == survivorId)` to decide whether the survivor already has a profile of
   that kind. With an archived survivor the filter hides its profile, so `survivorHasOne` is `false`,
   so a merged profile is repointed onto an activity that already has one — and `ActivityId` is
   **unique** on all three profile tables. The insert violates the index, the transaction rolls back,
   and the user sees a 400/500 on an irreversible action they just confirmed.
   `ActivityMergeTests.Merge_IntoAnArchivedSurvivor_IsAllowed_AndLeavesItArchived:369` passes because
   its survivor carries no profile.

The grid path is probably safe — `ApplyCustomFiltering` lifts the named filter on the outer query
(`GridActivityEndpoint.cs:84-85`) and EF applies a named `IgnoreQueryFilters` across the whole
compiled tree including correlated subqueries — but that is an implementation detail nothing asserts,
and it is the opposite of the guarantee the seam's own remarks give. Verify it rather than assume it.

**Suggested fix.** Decide the rule in Core and write it into `IActivityReferenceSource`'s contract:
*"references are counted regardless of archival state"* is the only rule consistent with
`canDelete`'s meaning. Then either lift `AppQueryFilters.ActivityArchived` inside
`ActivityReferenceService.ReferencingActivityIds` / `CountByActivityAsync` (one place, all sources),
or require each implementation to lift it — the former is far less likely to rot. The
`survivorHasOne` check in ActivityProfiles must lift it either way.

**Doc impact:** `../application/seam/README.md` → "How each one fails" (add the archival-filter mode
to `IActivityReferenceSource`); `domain-map.md` → Invariants #7 (a seam's `IQueryable` must stay
composable **and** must be explicit about which ambient filters it runs under).

---

### `SEC-5` 🟡 `User.WeatherLocation` is personal data with no `[AuditIgnore]`

`domain/model/entity/user/User.cs:20-31`. Its own doc comment says *"Do not log this value… it is
still personal data and log files survive a GDPR erasure"*, and the two credential fields immediately
above it carry `[AuditIgnore]` (`:12,15`). `WeatherLocation` does not, and neither do `Email` /
`UserName` (inherited from `BaseUser`). Auditing is not wired in this app today — so nothing is
written and this is inert — which is precisely why it is cheap to fix now and easy to miss on the day
someone turns the interceptor on.

**Suggested fix.** `[AuditIgnore]` on `WeatherLocation`; decide `[NoAudit]` on `User` as a whole,
since almost every column on it is either a credential or personal data.

**Doc impact:** none.

---

## 6. Code quality

### `CQ-1` 🟠 `icon` passes validation at 255 chars and fails in Postgres at 51

`application/validator/NameTextColorIconValidator.cs:21-22` allows `MaximumLength(255)`. The column is
`varchar(50)` — `framework/Sydowwe.Framework/infrastructure/persistence/configuration/extensions/NameTextColorEntityConfigurationExtension.cs:11,18`
(`builder.Property(r => r.Icon).HasMaxLength(50)`). Any icon of 51–255 characters clears FluentValidation
and hits Postgres error 22001, surfacing through `DbUtils.HandleException` as a generic failure rather
than a field-level message the form can attach to `icon`. Affects `POST/PUT /activity-role` and
`POST/PUT /activity-category`. (`Name` 50-vs-100 and `Text` 500-vs-1000 are mismatched in the safe
direction; only `Icon` is inverted.)

**Suggested fix.** `MaximumLength(50)` on `Icon` in the validator. While there: the three other
lengths should be sourced from one place rather than restated in both layers.

**Doc impact:** none.

---

### `CQ-2` 🟠 `TimeDtoValidator` never runs, and the requests that rely on it are unvalidated

`application/validator/TimeDtoValidator.cs:7-13`. FastEndpoints resolves validators by the endpoint's
**request** type. No endpoint anywhere takes a bare `TimeDto` as its request, so this validator is
never instantiated — it is dead code that looks like coverage. `DateRangeDto` documents this exact
trap two files away (`application/dto/dto/DateRangeDto.cs:89-101`: *"an abstract or open-generic base
is never discovered on its own and a request type without its own concrete validator is silently
unvalidated. That is what these rules were until now: they were written and never called from
anywhere"*) — and the same mistake was then made again for `TimeDto`.

The consequence is live, not theoretical. Planning works around it by re-implementing
`t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59` inline in **seven** validators
(`ApplyTemplateToTaskPlannerValidator.cs:25,29`, `CalendarBatchValidators.cs:49,53`,
`CalendarRequestValidator.cs:12,16`, `PatchPlannerTaskStatusValidator.cs:14,19`,
`PlannerTaskChangeSpanValidator.cs:12`, …). History does not:
`AdhdTimeOrganizer.History/application/dto/request/dashboard/summary/HistorySummaryStackedBarsRequest.cs:8-9`
carries `WindowStartTime` / `WindowEndTime` as `TimeDto`, and its validator
(`HistorySummaryDateRangeValidators.cs:24-25`) only inherits the date-range rules. `TimeDto.ToTimeOnly()`
is `new TimeOnly(Hours, Minutes)`, which throws `ArgumentOutOfRangeException` — so
`{"windowStartTime":{"hours":99,"minutes":0}}` is an unhandled 500.

**Suggested fix.** Delete `TimeDtoValidator` and replace it with a static
`TimeDto.ApplyValidationRules<T>(validator, selector)` in the exact shape `DateRangeDto` already uses
(including the `OverridePropertyName` note, which exists for the same reason). Then call it from the
seven Planning validators and from the History ones that need it.

**Doc impact:** `domain-map.md` → `application/validator/` paragraph, which lists `TimeDtoValidator`
among "the three shared ones" as though it were live.

---

### `CQ-3` 🟠 The four hand-rolled activity endpoints diverge from the base-class ones on filters and on roles

`command/QuickEditActivityEndpoint.cs:10,31` and `command/CloneActivityEndpoint.cs:10,30` are raw
`Endpoint<>`s that load with `FindAsync`. `FindAsync` cannot lift a named query filter — which is why
`SetArchivedActivityEndpoint` explicitly uses a keyed `FirstOrDefault` instead and says so (`:57-61`).
The result is an inconsistency with no stated rationale:

| Route | Reaches an archived activity? |
|---|---|
| `GET /activity/{id}` | ✅ lifts (`GetByIdActivityEndpoint.cs:24`) |
| `PUT /activity/{id}` | ✅ lifts (`UpdateActivityEndpoint.cs:19`) |
| `DELETE /activity/{id}` | ✅ lifts (`DeleteActivityEndpoint.cs:16`) |
| `PATCH /activity/{id}/archived` | ✅ lifts (`SetArchivedActivityEndpoint.cs:63-65`) |
| `PUT /activity/{id}/quick-edit` | ❌ 404 |
| `POST /activity/{id}/clone` | ❌ 404 |

Every other row action in the archive view works; those two 404. Since `UpdateActivityEndpoint`'s
comment argues that lifting is safe *because the request cannot carry `IsArchived`* — and
`QuickEditActivityRequest` cannot either — the same argument applies verbatim.

Second half of the same finding: `CloneActivityEndpoint`, `QuickEditActivityEndpoint` and
`BaseActivityFormSelectOptionsEndpoint` never call `Roles(...)`. They are authorized only because
`Program.cs:573-576` installs a global configurator that applies `Roles("User","Admin","Root")` to any
endpoint with no role metadata. That is a working design, but it is invisible at the call site, it is
inconsistent with `MergeActivityEndpoint:60,65` and `SetArchivedActivityEndpoint:37,42` (which do
declare `AllowedRoles()`), and nothing in Core asserts the configurator still exists — see `TEST-10`.

**Suggested fix.** Give both endpoints `IgnoredQueryFilters => [AppQueryFilters.ActivityArchived]`
semantics by replacing `FindAsync` with a keyed `FirstOrDefaultAsync` + `IgnoreQueryFilters([…])`, and
add the `AllowedRoles()` / `Roles(...)` pair for symmetry with the other two custom endpoints.

**Doc impact:** `domain-map.md` → Navigation index rows for `QuickEditActivityEndpoint` and
`CloneActivityEndpoint`; `summary.md` → Extension playbook ("Add an endpoint") already tells you to
override `IgnoredQueryFilters` — these two are the counter-examples.

---

### `CQ-4` 🟡 `Activity.Clone()` copies the source's timestamps

`domain/model/entity/activity/Activity.cs:56-61`. `MemberwiseClone` resets `Id` only, so the copy
inherits `CreatedTimestamp` and `ModifiedTimestamp` from the original. Those properties are
initialised to `DateTime.UtcNow` in C# (`BaseTableEntity.cs:6-7`) and configured with
`HasDefaultValueSql("now()")`, so EF always sends the value it holds — meaning the clone is persisted
claiming it was created whenever the original was. Harmless today (nothing sorts activities by
creation), wrong the first time something does. `row_version` is a shadow property and is *not*
copied, so the concurrency token is fine.

**Suggested fix.** Reset both timestamps in `Clone()` alongside `Id`, or construct the copy explicitly
instead of using `MemberwiseClone`.

**Doc impact:** none.

---

### `CQ-5` 🟡 `GetSelectOptionsActivityEndpoint.Filter` re-implements the global archival filter

`query/GetSelectOptionsActivityEndpoint.cs:23` — `query.Where(a => !a.IsArchived)`. The
`ActivityArchived` global filter is already applied and this endpoint never lifts it, so the predicate
is a no-op. It is also exactly the shape `BaseActivityFormSelectOptionsEndpoint.cs:60-62` warns
against in a comment ("a hand-written predicate would leave the filter still applied on the opt-in
path"), which makes it a bad example sitting next to its own warning.

**Suggested fix.** Delete the override; keep the docstring, which is the valuable part.

**Doc impact:** none.

---

### `CQ-6` 🟡 Dead `.Include()` calls before a projection

`endpoint/base/BaseActivityFormSelectOptionsEndpoint.cs:68-69` — `.Include(a => a.Category)` and
`.Include(a => a.Role)` immediately precede a `.Select(...)` into an anonymous type. EF ignores
includes in a projection; the columns come from the `Select`. They cost nothing at runtime and
mislead every reader about what the query loads.

**Suggested fix.** Remove both lines.

**Doc impact:** none.

---

### `CQ-7` 🟡 `GET /activity-role/by-Name/{value}` is still published

`query/GetByNameActivityRoleEndpoint.cs`. `SystemActivityRole` exists specifically because looking a
role up by its user-editable display name broke quick-create for anyone who renamed one — the enum's
docstring (`domain/model/enum/SystemActivityRole.cs:9-12`) tells that story, and
`GetBySystemKeyActivityRoleEndpoint` is described as "the identity-based replacement". The replaced
endpoint is still routed and has no test (`TEST-12`). Not a defect on its own; it is a live surface
that reintroduces the failure the module was fixed for, one client call at a time.

**Suggested fix.** Confirm no SPA call site remains, then delete it. If one remains, keep it and note
in `domain-map.md` that it is retained for a named caller.

**Doc impact:** `domain-map.md` → Navigation index (Role reads row) if it goes.

---

### `CQ-8` 🟡 `PomodoroTimerPresetRequest.ToEntity` sets `UserId = 0`

`application/dto/request/timer/PomodoroTimerPresetRequest.cs:34`, where the sibling
`TimerPresetRequest.ToEntity:12` does not. Framework's own comment on the property calls `UserId = 0`
"exactly the value that produces the FK violation it looked like it was preventing"
(`framework/…/domain/entity/user/BaseEntityWithUser.cs:8-11`). It is stamped on insert, so the zero is
overwritten and nothing breaks — but it is a placeholder the framework explicitly removed the need
for, and the inconsistency between the two sibling DTOs is the kind that gets copied.

**Suggested fix.** Drop the line.

**Doc impact:** none.

---

### `CQ-9` 🟡 `DefaultUsersSeeder` logs Identity failures without the reasons

`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:69-72,75-77,46-52`. Four
`logger.LogError("Failed to …")` calls, none passing `result.Errors`. When the root admin fails to
seed — the app's only bootstrap path — the log says it failed and not why. `IdentityResult.Errors`
carries codes and descriptions and contains no PII (the email is already excluded, and
`Seed_DuplicateEmailFailure_NeverLogsTheRawEmailAddress` pins that it stays excluded).

**Suggested fix.** Log `string.Join("; ", result.Errors.Select(e => e.Code))` — codes only, so the PII
test keeps passing by construction.

**Doc impact:** none.

---

### `CQ-10` 🟡 The root admin's time zone is the server's

`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:27` — `Timezone = TimeZoneInfo.Local`.
`IUserTimeZoneResolver`'s contract goes out of its way to reject exactly this
(`domain/serviceContract/IUserTimeZoneResolver.cs:14-17`: *"UTC is the deliberate fallback rather than
`TimeZoneInfo.Local`: a missing user must not make the answer depend on which machine the server
happens to be running on"*). The seeded account's dashboards therefore read differently depending on
which host ran the seeder, which is the same defect the resolver refuses to have.

**Suggested fix.** `TimeZoneInfo.Utc`, or read `ROOT_ADMIN_TIMEZONE` with a UTC default.

**Doc impact:** none — though it is worth a line in `domain-map.md` → Business rules beside the
existing "A missing user's time zone resolves to UTC" rule.

---

### `CQ-11` 🟡 The dev seeders' truncate is app-wide; the comment says it is per-user

`infrastructure/persistence/seeder/dev/ActivitySeeder.cs:16-19` and `:23` —
`TruncateTableCascadeAsync<Activity>()` empties the table for **every** user, and then
`// Note: TruncateTable already removed existing data for this user` describes it as scoped. Same in
`ActivityRoleSeeder.cs:17-20` and `ActivityCategorySeeder.cs:17-20`. Correct behaviour for the
framework's dev-reset flow (which truncates in descending `Order` before re-seeding), wrong in the
comment, and the comment is the thing a future reader will act on. The truncate itself is
schema-qualified from the model (`framework/…/seeder/SeederExtensions.cs:79-96`), so the CLAUDE.md
schema hazard does not apply here.

**Suggested fix.** Correct the comment in all three files to say the table is emptied for every user
and that only the *re-seed* is per-user.

**Doc impact:** none.

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GridActivityEndpoint.cs:73-74` | `UsageCount` and `CanDelete` are **two** correlated aggregates over the same twelve-table `UNION ALL`, per row | 2 × 200 = 400 correlated scans per page instead of 200; the endpoint's own `<remarks>:56-59` budgets for one | `ActivityResponse.cs:36-41` already states `CanDelete` is exactly `UsageCount == 0` today. Project the count once into a local and derive the boolean, or `.Select` into an intermediate shape | `PERF-1` |
| `GridActivityEndpoint.cs:90-103`, `GridActivityRoleEndpoint.cs:15-25`, `GridActivityCategoryEndpoint.cs:29-33` | `.Contains(...)` on `Name` / `Text` / `RoleName` / `CategoryName` / `Icon` → `LIKE '%x%'` | Unindexable sequential scan on the hub table, **and case-sensitive in Postgres** — searching `deep` finds nothing when the row says `Deep Work`. The UX half is the bigger problem | `EF.Functions.ILike(x, $"%{v}%")`, plus a `pg_trgm` GIN index on `activity.name` if the grid stays free-text | `PERF-2` |
| `ActivityConfiguration.cs:19,23,37-42` | Five indexes on `Activity`: unique `(UserId,Name)`, `(UserId,IsArchived)`, `RoleId`, `CategoryId`, `(UserId,RoleId)`, `(UserId,CategoryId)` | `RoleId` / `CategoryId` alone are largely subsumed by the composites for this app's access patterns (everything is user-scoped first); six indexes on the most-written table in the schema | Drop the two standalone FK indexes after confirming no unscoped join needs them; keep the composites | `PERF-3` |
| `ActivityCategoryGridResponse.cs:49` | `string.Join` over a `LEFT JOIN LATERAL` per row | Documented and accepted (`:19-26`) — the `RoleSortKey` proxy exists for it. Noted so it is not re-discovered | none | `PERF-4` 🟡 |
| `ActivityReferenceService.cs:31` | `sources.ToList()` in the constructor materialises the DI enumerable per request scope | Negligible (7 entries), and it is what makes `_sources.Count == 0` cheap | none | — |

### Indexes that should exist

Nothing is missing for correctness. Two candidates, both conditional:

- **`pg_trgm` GIN on `activity.name`** — only worth it if `PERF-2`'s `ILike` change lands and grids
  stay free-text. Below a few thousand activities per user the sequential scan is cheaper than the
  index.
- **Nothing for `activity_role.system_key`** — the filtered unique `(user_id, system_key)`
  (`RoleConfiguration.cs:27-29`) already serves `GetBySystemKeyActivityRoleEndpoint` as a covering
  lookup.

The `IsManyWithOneUser()` calls on both preset tables plus the explicit `HasIndex(t => t.UserId)`
(`TimerPresetConfiguration.cs:25`, `PomodoroTimerPresetConfiguration.cs:46`) may be creating the same
index twice — worth one look at the snapshot, not worth a finding until confirmed.

---

## Code/doc drift

| ID | Where the docs and the code disagree | Which side is wrong | Pairs with |
|---|---|---|---|
| `DOC-1` | `domain-map.md` → Invariants #3 states "`Activity.RoleId` is required; `CategoryId` is optional. Both cascade" as a neutral fact, and no Business rule covers deleting a role or a category. The Business rules section discusses cascade blast radius **only** for deleting an activity | **Both.** The code should stop cascading through an optional FK; the doc should state the blast radius and say where the `usageCount` promise ends | `SEC-1` |
| `DOC-2` | `domain-map.md` → Invariants #6: "Per-user scoping is the DbContext's job … not the endpoints (`ApplyUserScoping` is a no-op virtual)". True for reads; read as a general licence it authorises `SEC-2` | **Doc.** Add "reads only — any caller-supplied FK id must be checked against the caller's own rows". Same sentence flagged as `DOC-5` in `review/todo-lists/02-findings.md` | `SEC-2` |
| `DOC-3` | `IActivityReferenceSource.cs:62-67` asserts the only ambient concern is user scoping and that "no source here needs a hand-written scope". There is a second ambient filter — `ActivityArchived` — on three of the entities the ActivityProfiles source reads | **Both.** Decide the rule (references count regardless of archival state), implement it centrally, and rewrite the remark | `SEC-4` |
| `DOC-4` | `domain-map.md` → Business rules → merge: "an archived survivor is allowed" — silent on archived *merged* ids, which today fail the whole merge | **Code.** Lift the filter and then make the doc say both directions are allowed | `SEC-3` |
| `DOC-5` | `domain-map.md` → `application/validator/` lists `TimeDtoValidator` among "the three shared ones", implying it runs. It cannot | **Both.** Replace the validator with a rules helper, then update the sentence | `CQ-2` |
| `DOC-6` | `docs/testing.md` → "Known gaps": *"Nothing in Core is currently carried as an expected-failing `[Trait("Status","KnownGap")]` test"*. Literally true, but `ActivityForeignKeyInventoryTests.cs:72-84` carries a `TODO` / "⚠ OPEN QUESTION" on the three preset cascades — a known gap in everything but the trait | **Doc.** List it under "Two standing caveats" so it is visible from `testing.md` rather than only from the test literal | `MIG-3` |
| `DOC-7` | `summary.md` → Gotchas describes `IsArchived` and the pickers thoroughly, but nowhere says that archiving is available on activities **only** — roles, categories and presets have no retire path, so the only way to remove a stale role is the destructive one | **Doc.** One sentence in Gotchas | `SEC-1` |
