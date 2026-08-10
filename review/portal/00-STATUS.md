# Portal review — run status (incomplete)

**Run halted 2026-08-09 by an API session limit**, not by completion. Every in-flight
subagent was terminated; some wrote their fragment first, some did not.

## What exists

- `findings/` — **42** per-file fragments, one per reviewed source file.
  Naming is **inconsistent** across fragments (some carry an `AdhdTimeOrganizer__`
  prefix, some don't) because the agents flattened paths against different roots.
  Normalize before synthesis; don't treat the two spellings as different files.
- `REMAINING.txt` — the **670** files not yet reviewed, one repo-relative path per line.

## Deliverables — written, but from 42 files only

The three review files were synthesized from the 42 surviving fragments at the user's explicit
request, after the scope limitation was raised. **Every one carries a scope banner**; the coverage
number and the risk ranking describe the reviewed 6%, not the portal.

- `01-testing.md` — test infra, quality, coverage matrix, `TEST-1`…`TEST-19`
- `02-findings.md` — `SEC-1`…`SEC-14`, `CQ-1`…`CQ-38`, `PERF-1`…`PERF-13`, `DOC-1`…`DOC-6`
- `03-risks-rollout.md` — risk ranking, `MIG-1`…`MIG-10`, `AUDIT-1`…`AUDIT-4`
- `testingPrompts/` — 8 paste-ready prompts (of 19 `TEST-n` ids; the other 11 need the unreviewed
  endpoint/validator/DTO surface first and would otherwise be guesswork)

**Re-run the drift section after the endpoint pass.** `AdhdTimeOrganizer/docs/` (README ·
summary · domain-map · testing) landed *during* the run, so the fragments were judged against
CLAUDE.md conventions; only the `DOC-n` section in `02-findings.md` was derived against
`domain-map.md`, and only for the reviewed files.

Several findings are explicitly marked **unverified** — they name a risk whose confirming file was
never read because its agent was killed: `DOC-6`/`MIG-8` (RoutineTimePeriodSeeder `Collides` vs two
unique indexes), `MIG-6` (partition exhaustion on the two tracking tables), `MIG-7` (unique index on
`RoutinePeriodCompletion`), and `PERF-10`…`PERF-13` (missing indexes). Check these first — they are
cheap and two of them are potential outages.

## Scope decisions made during the run

- Target was the **whole portal** (`AdhdTimeOrganizer/`), fanned out one agent per
  file — the user's explicit choice over reviewing a documented framework module.
- **Excluded:** `infrastructure/persistence/Migrations/` (EF-generated), `reference/mojaCore/`
  (foreign code), `bin`/`obj`.
- **Skipped as zero-yield:** 19 pure declaration files — `domain/model/enum/*`, the two
  `domain/model/entityInterface/` markers, the three `domain/serviceContract/` +
  `domain/extServiceContract/` interfaces.
- **Docs:** the portal's `AdhdTimeOrganizer/docs/` (README · summary · domain-map · testing)
  was being written in parallel and did not exist when the fan-out started. Fragments were
  therefore judged against **CLAUDE.md conventions**, not against a domain-map spec.
  At synthesis, read `AdhdTimeOrganizer/docs/domain-map.md` and re-derive the `DOC-n`
  (code/doc drift) section against it — that section is currently unbacked.
- Seeder fragments dispatched late in the run were judged against the **newer**
  CLAUDE.md `BasePerUserDefaultSeeder` / `Collides` / `IgnoreQueryFilters` rules,
  which landed mid-run. Earlier fragments predate those rules.

## Confirmed finding, recorded here because it has no fragment

**DOC-1 — `AdhdTimeOrganizer/application/helper/PortalEndpointHelper.cs` does not exist.**
CLAUDE.md describes it in detail (re-exporting `GetUserOrHigherRoles()` /
`GetAdminOrHigherRoles()` and adding `HttpContext.GetVerifiedUser()` closed over the portal
`User`). A glob for `**/PortalEndpointHelper.cs` and a full-tree text search for
`PortalEndpointHelper`, `GetVerifiedUser` and `GetUserOrHigherRoles` all returned nothing;
`application/helper/` contains only `TaskPlannerHelper.cs`. Either the file was never created
or it was removed without updating CLAUDE.md. Decide which side is wrong.
Doc impact: CLAUDE.md → FastEndpoints Base Classes (the `PortalEndpointHelper` paragraph).

## To resume

1. Re-dispatch `code-reviewer` over `REMAINING.txt`, in priority order:
   `application/endpoint/**` (275 — where IDOR/user-scoping findings live) →
   remaining `infrastructure/persistence/configuration/**` and `seeder/**` →
   `application/validator/**` → `application/dto/**`.
2. Pin the fragment filename in each prompt (repo-root-relative, `/` → `__`); agents
   drift otherwise.
3. Then synthesize `01`–`03` from `findings/`, minting stable IDs at that point.
