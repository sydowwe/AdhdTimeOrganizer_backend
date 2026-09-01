# AdhdTimeOrganizer.Core — 03 · Risks & rollout

Point-in-time snapshot, 2026-08-31. IDs reference [02-findings.md](02-findings.md) and
[01-testing.md](01-testing.md); nothing is restated here.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 1 | 🔴 ✅ fixed 2026-09-01 | Deleting an activity role or category cascade-destroys every activity under it, and through the activity cascade every history row, planner task, to-do item, profile and preset that pointed at them — 204, no warning, no `canDelete` | `SEC-1` | `infrastructure/persistence/configuration/activity/ActivityConfiguration.cs:25-34` |
| 2 | 🟠 | Every Core write accepts another user's FK ids: existence oracle, rows that vanish from their owner's grid, and cross-user cascade deletion behind a `canDelete: true` | `SEC-2` | `application/dto/request/activity/ActivityRequest.cs:12-24` |
| 3 | 🟠 | The reference seam runs under the archival filter it never lifts: `usageCount: 0` on an archived activity that has profiles, and a merge into an archived survivor with a profile violates a unique index and rolls the whole merge back | `SEC-4` | `application/seam/IActivityReferenceSource.cs:62-67` |
| 4 | 🟠 | `POST /activity/merge` 404s on an archived merged id, which is exactly what the All view offers | `SEC-3` | `application/endpoint/activity/activity/command/MergeActivityEndpoint.cs:91` |
| 5 | 🟠 | `icon` of 51–255 chars passes validation and fails in Postgres as an untargeted error | `CQ-1` | `application/validator/NameTextColorIconValidator.cs:21-22` |
| 6 | 🟠 | `TimeDtoValidator` never runs; History's stacked-bars window times are unvalidated and `hours: 99` is a 500 | `CQ-2` | `application/validator/TimeDtoValidator.cs:7-13` |
| 7 | 🟠 | Quick-edit and clone 404 on an archived activity while update, get-by-id and delete all lift the filter; three custom endpoints declare no roles at all | `CQ-3` | `application/endpoint/activity/activity/command/QuickEditActivityEndpoint.cs:31` |
| 8 | 🟠 | `usageCount` and `canDelete` are two separate correlated aggregates over the same twelve-table union, per row | `PERF-1` | `application/endpoint/activity/activity/query/GridActivityEndpoint.cs:73-74` |
| 9 | 🟡 | Grid text filters are case-sensitive `LIKE '%x%'` — a UX defect first, an index problem second | `PERF-2` | `application/endpoint/activity/activity/query/GridActivityEndpoint.cs:90-103` |
| 10 | 🟡 | `User.WeatherLocation` is documented personal data with no `[AuditIgnore]` | `SEC-5` | `domain/model/entity/user/User.cs:31` |
| 11 | 🟡 | `Activity.Clone()` persists the source's created/modified timestamps | `CQ-4` | `domain/model/entity/activity/Activity.cs:56-61` |
| 12 | 🟡 | Six indexes on the most-written table; two are largely subsumed by the composites | `PERF-3` | `infrastructure/persistence/configuration/activity/ActivityConfiguration.cs:37-42` |
| 13 | 🟡 | Redundant hand-written archival predicate next to the comment warning against it | `CQ-5` | `application/endpoint/activity/activity/query/GetSelectOptionsActivityEndpoint.cs:23` |
| 14 | 🟡 | Dead `.Include()`s before a projection | `CQ-6` | `application/endpoint/base/BaseActivityFormSelectOptionsEndpoint.cs:68-69` |
| 15 | 🟡 | `by-Name` role lookup still routed — the surface `SystemActivityRole` replaced | `CQ-7` | `application/endpoint/activity/role/query/GetByNameActivityRoleEndpoint.cs` |
| 16 | 🟡 | `UserId = 0` placeholder the framework explicitly removed the need for | `CQ-8` | `application/dto/request/timer/PomodoroTimerPresetRequest.cs:34` |
| 17 | 🟡 | Root-admin seed failures log "Failed to…" without `IdentityResult.Errors` | `CQ-9` | `infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:69-77` |
| 18 | 🟡 | Root admin seeded with `TimeZoneInfo.Local` — the exact fallback `IUserTimeZoneResolver` refuses | `CQ-10` | `infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:27` |
| 19 | 🟡 | Dev seeders truncate app-wide; three comments claim per-user | `CQ-11` | `infrastructure/persistence/seeder/dev/ActivitySeeder.cs:16-23` |

Doc drift `DOC-1`…`DOC-7` is tracked in [02-findings.md](02-findings.md#codedoc-drift); each is
attached to the finding whose fix must carry it.

## 10. Migration / rollout risks

> **`MIG-1` and `MIG-2` shipped together on 2026-09-01, against this table's advice.** It recommended
> landing the `SetNull` migration alone and putting `usageCount` on `ActivityRoleResponse` *before* role
> deletion starts refusing, so the grid could grey the button first. `SEC-1` was fixed as one commit
> instead: FK, endpoint refusal, both lookups' `usageCount` / `canDelete`, and the tests. The staging
> the table asks for buys nothing here — the only caller is this repo's own SPA, and it takes the
> projection and the refusal from the same deploy, so there is no window in which the button is enabled
> against a backend that refuses. `MIG-3` (the three preset cascades) is **still open** and was
> deliberately not folded in; the `TODO` block in `ActivityForeignKeyInventoryTests` stands.

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **`Activity.CategoryId` → `SetNull`** (`SEC-1`). `ALTER TABLE … DROP CONSTRAINT` + `ADD CONSTRAINT` takes an `ACCESS EXCLUSIVE` lock on `activity.activity` and re-validates every row. The table is small per user but is the hub every slice joins to | High that it is needed; low that it hurts — seconds on a table of this size | Single migration, no data change (no existing row's `category_id` value moves). Run it with the app up; the lock window is short. **Do not** combine it with the role-side change | `MIG-1` |
| **Role deletion becomes a 409** (`SEC-1`). A currently-succeeding `DELETE /activity-role/{id}` starts refusing when activities exist. Any script or SPA flow that deletes-then-recreates a role breaks | Medium — the SPA's Settings grid is the only known caller, and it already handles the `SystemKey` 409 | Reuse the existing 409 path in `DeleteActivityRoleEndpoint` so the client needs no new branch, only a new message. Ship the `usageCount` projection on `ActivityRoleResponse` **first**, so the grid can grey the button before the backend starts refusing | `MIG-2` |
| **The three preset cascades stay undecided** — `PomodoroTimerPreset.Focus/RestActivityId` and `TimerPreset.ActivityId` are `Cascade` on nullable columns, frozen as an explicit `TODO` in `ActivityForeignKeyInventoryTests.cs:72-84`. *Previously known; listed because `SEC-1` is the same question one level up and the two should be answered together, not in two passes* | Certain to recur | Decide `SetNull` vs `Cascade` for all five nullable activity-side FKs in one sitting (three preset columns + `Activity.CategoryId`). Whatever is decided, update the frozen literal in the same commit — the test fails until you do, which is the intended forcing function | `MIG-3` |
| **FK-ownership checks turn 201s into 404s** (`SEC-2`). If any cross-user pointer already exists in a live database, the owning row keeps working but the next *edit* of it starts 404ing | Low (needs a deliberate cross-user post) but silent | Before shipping, run a read-only audit query per FK — `activity.role_id` / `category_id` and the three preset columns joined back to `user_id` — and count mismatches. Repoint or null them in a data migration if any exist; do not let the endpoint change be the thing that discovers them | `MIG-4` |
| **Lifting `ActivityArchived` inside the reference seam** (`SEC-4`) changes `usageCount` for archived activities from "under-reported" to correct — so `canDelete` flips from `true` to `false` on rows where the UI currently offers a delete button. That is the fix, but it is a visible behaviour change on a screen with no release note | Medium | Ship it with the merge fix (`SEC-3`) as one "archived activities behave like active ones" change, and say so. `ActivityArchivingTests` is the right place for the regression pins | `MIG-5` |
| **`ILike` migration** (`PERF-2`) changes result sets: searches that returned nothing start returning rows. Any saved filter or e2e test asserting emptiness breaks | Low | Land it alone, not bundled with an index change. Assert the new behaviour in `TEST-9` first | `MIG-6` |
| **Drift: `AppQueryFilters` lives in Core under the host's namespace** (`AdhdTimeOrganizer.infrastructure.persistence`, `infrastructure/persistence/AppQueryFilters.cs:1`). Any refactor that "tidies" the namespace churns ~40 host call sites and, worse, looks harmless | Low but expensive | Documented in `summary.md` → Gotchas ("Don't 'fix' it"). Leave it; if the fixes above touch the file, do not touch line 1 | `MIG-7` |
| **Drift: Core's entities map to schemas by *namespace*, not assembly** (`ModuleSchemas.SchemaByCoreNamespace`; three schemas — `user`, `activity`, `public`). Any of the refactors above that moves a type between `entity.user` / `entity.activity` / `entity.timer` silently moves its **table** | Low, catastrophic if hit | `ModuleSchemaTests` catches an *unmapped* namespace, not a *moved* one. None of the fixes in this review require moving a type — keep it that way, and if one does, treat it as a data migration | `MIG-8` |
| **Drift: the merge and reference-count fixes span two repos.** `SEC-4`'s central fix lands in Core; the `survivorHasOne` half lands in `AdhdTimeOrganizer.ActivityProfiles`; neither touches `framework/`. But `CQ-1`'s real fix (single source for column lengths) and `CQ-2`'s (a `TimeDto` rules helper) both point at the **submodule** | Medium | Per CLAUDE.md: commit and push in `framework/` **first**, then commit the new gitlink here. An uncommitted framework edit is invisible in the parent's `git status` beyond "modified: framework (new commits)" | `MIG-9` |

## Audit-log gaps

**No bulk-operation gaps.** Core contains **zero** `ExecuteUpdateAsync` / `ExecuteDeleteAsync` calls —
every write goes through the ChangeTracker via the `DbContextHelper` Result helpers
(`AddEntityAsync` / `UpdateEntityAsync`) or through `RemoveRange` + `SaveChangesAsync` in
`MergeActivityEndpoint:111-112`. Whatever interceptor is eventually wired up will see all of it. The
seam's `RepointAsync` implementations are likewise entity-tracked by contract
(`IActivityReferenceSource.cs:70-84` — mutate the caller's context, never save), which is a rare case
of a design choice for atomicity also being the right choice for auditing.

| ID | Gap | Sev |
|---|---|---|
| `AUDIT-1` | `User.WeatherLocation` carries no `[AuditIgnore]` despite its own doc comment classifying it as personal data that must not be logged; `Email` / `UserName` (from `BaseUser`) carry none either, while the two Google credential fields do. `domain/model/entity/user/User.cs:12,15,31`. See `SEC-5` | 🟡 |
| `AUDIT-2` | The declarations already present are **inert** — auditing exists in Framework and is not wired into this app, so nothing is written today and no test asserts on audit rows. `docs/testing.md` says so explicitly. The risk is not that auditing is wrong; it is that `[AuditIgnore]` looks like protection during review and provides none, so the next person adding a sensitive column to `User` or `Activity` has no failing test to catch a missing attribute | 🟡 |
| `AUDIT-3` | `MergeActivityEndpoint` is the one genuinely irreversible operation in Core — it deletes activities and moves rows across every slice inside one transaction — and it writes no record of what it did beyond the `MergeActivityResponse` sent to the caller. When auditing is wired, this is the first endpoint that needs an explicit `IAuditService.LogAsync` rather than interceptor-derived rows: the interceptor will capture the deletes and the FK updates, but not the *association* between them | 🟡 |

**Suggested sequencing.** `AUDIT-1` is a one-line change worth making now, while the context is
loaded. `AUDIT-2` and `AUDIT-3` are notes for whoever wires the interceptor and should not be acted on
before then.
