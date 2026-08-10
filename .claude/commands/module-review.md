# Code Review — $ARGUMENTS

> **`$ARGUMENTS`** = the module under review (e.g. `Attendance`). **`<module>`** in
> the paths below = its lowercase kebab form (e.g. `attendance`). If `$ARGUMENTS`
> is empty, ask which module to review.

You are doing a deep, single-pass code review of the `$ARGUMENTS` module and
producing three written artifacts. This is a delta-and-deep-dive review: get the
**most** out of the module files while they are loaded in context — once a file is
read, mine it for everything (endpoint shape, invariants, findings, test gaps,
doc drift) before moving on. Do not re-read.

This review **consumes the module's living docs** (`summary.md`, `domain-map.md`)
rather than regenerating the domain map — see "Inputs" below. It does NOT produce
a domain map; that's the docs' job. The review's outputs are point-in-time
snapshots (findings, coverage, risks) and are disposable.

## Scope
- `application/.../<module>`, `domain/.../<module>`, `infrastructure/.../<module>`,
  plus the matching tests.
- Pull in adjacent modules ONLY where `$ARGUMENTS` depends on them for an
  invariant — name them explicitly when you do.
- External/regulatory spec, if any (e.g. Slovak Labour Code for Attendance) —
  otherwise common-sense review only.

## Working method (read this first)

This is a **single deep-read pass**, not a fan-out. The good findings in a review
like this come from *cross-file* insight ("this endpoint bypasses that invariant",
"this service has zero tests", "this entity has no DB constraint backing the
invariant the code assumes"). That only happens if one reviewer holds the whole
module map at once. So:

0. **Orient from the docs (Inputs).** Read the module's `summary.md` and
   `domain-map.md` first (see "Inputs" below). Use the navigation index to *target*
   which source files matter — don't rebuild the map.
1. **Inventory pass.** `Glob` the module tree. Read every source + test file once.
   As you go, keep a running scratch list in context of: endpoints,
   services/handlers/jobs/seeders/validators, any smell you spot, and **any place
   the code contradicts the docs** (tag each with a stable ID — see below). Don't
   write findings to chat yet.
2. **Delta awareness.** Before flagging anything, scan for prior review files
   (`review/`, `*Review.md`, `fixed*.md`) and for `[Trait("Status","KnownGap")]`
   tags in tests. Do NOT re-flag items already fixed or already pinned as known
   gaps — note them as "previously known" and move on. This is a delta review.
3. **Emit in dependency order.** Write the three files below in order (01 → 03).
   Each later file may reference earlier files but must NOT require re-reading
   source. File 03's tables reference finding IDs minted in file 02.
4. **Subagent escape hatch.** Only if the module is too large to hold in one
   context, dispatch per-file reviews to subagents that return a short structured
   summary. Default is single-pass — do not fan out for a normal-sized module.
5. **Output discipline.** All artifacts go under `review/<module>/`. Chat output
   = a short index of what was written + the top 5 risks. Nothing else.

## Stable finding IDs

Every issue gets an ID so files can cross-link: `SEC-n` (security/authz),
`CQ-n` (code quality), `PERF-n` (performance), `TEST-n` (test gap),
`MIG-n` (migration/rollout), `AUDIT-n` (audit-log gap), `DOC-n` (code/doc drift —
the docs assert something the code no longer does, or vice versa). The risk-ranked
list and migration table in file 03 reference these IDs — never restate the full
finding.

**Every finding carries a `Doc impact:` line** naming the living doc + section a
fix would need to touch, or `none`. Most are `none` (bug fixes rarely change
invariants/rules/index). This tells whoever applies the fix exactly what to
reconcile, in the same pass, while context is loaded — so the docs never silently
drift. Examples:
- `Doc impact: none`
- `Doc impact: domain-map.md → Invariants (carry-over consumption order)`
- `Doc impact: domain-map.md → Navigation index (new endpoint row)`

**Severity legend:** 🔴 blocker (legal / security / data-corruption) ·
🟠 important (wrong behavior or maintainability hole) · 🟡 polish/nit.

## Project conventions to actively check (from CLAUDE.md)

Treat each as a checklist item while reading — these are where this codebase
actually breaks:
- **Base classes used where they should be?** Custom endpoint that could be a
  `BaseGridEndpoint` / `BaseGetByIdEndpoint` / `BaseCreateEndpoint` etc. → flag as
  a refactor (`CQ-n`). If custom, is the custom-ness justified (tx, balance logic)?
- **Builder extensions** — entity configs should use `BaseEntityConfigure`,
  `EnumColumn`, `PriceColumn`, `IsManyWithOneUser`, etc., not hand-rolled
  `ToTable`/`HasKey`/row_version.
- **Audit interceptor** — any `ExecuteUpdateAsync` / `ExecuteDeleteAsync` bypasses
  the ChangeTracker and the audit interceptor → `AUDIT-n`. Sensitive props need
  `[AuditIgnore]`; whole entities `[NoAudit]`.
- **Result pattern** — CRUD via the `DbContextHelper` Result-returning helpers, not
  raw `SaveChangesAsync`.
- **DTO boundaries** — entities not leaking through endpoints; time values use
  `MyIntTime`, not raw ints/`TimeSpan`.
- **Concurrency** — `row_version` token respected on update paths.
- **User-scoping** — user-scoped reads filter by `User.GetId()`; `{id}` endpoints
  assert ownership (IDOR).
- **Partitioning** — partitioned tables only need `IsPartitionedByRange`; new years
  configured before year-end.

---

# Inputs — the module's living docs (read, don't regenerate)

This review does NOT produce a domain map. It consumes the module's living docs as
its map and oracle:

- **`summary.md`** — orient: purpose, dependency seams, gotchas.
- **`domain-map.md`** — the model, invariants (with DB-vs-app enforcement notes),
  business rules, glossary, and the navigation index. Use the index to target your
  reads. Treat the invariants and business rules as the **spec** — anywhere the
  code contradicts them is a `DOC-n` finding (decide per case whether the code or
  the doc is wrong).

**If these docs don't exist:** stop and recommend running the documentation pass
first (`/document-module $ARGUMENTS`) — the map is more valuable as a durable doc
than as throwaway review output. Only if explicitly told to proceed without docs,
build a lightweight scratch map in context (do not write it as an artifact) and
note in chat that the module is undocumented.

---

# FILE 1 — `review/<module>/01-testing.md`

### Test infrastructure summary
One paragraph: fixtures, auth handlers, base test classes in use (per the Testing
section of CLAUDE.md). Note anything bespoke or fragile.

### 7. Test quality assessment
AAA, isolation, fixture hygiene, flakiness risks, and — critically — **assertion
quality vs theater** (tests that run code but assert nothing meaningful). Call out
both strengths and weaknesses; cite specific test files.

### 8. Coverage matrix (endpoint-level + service-level)
- Endpoint table: `Endpoint | Happy | Edge | Auth | Test file` with ✅/❌ and counts.
- Separate short list for services/handlers/jobs/seeders with their test counts.
- End with **one honest "you can ship this" coverage number** and a sentence on
  where the riskiest gaps are (usually read/IDOR + rollover/batch paths).
- Each ❌ row gets a `TEST-n` ID.

### 12. Missing tests — backlog
Brief table of gaps keyed by `TEST-n`, each pointing to a paste-ready prompt file
written under `review/<module>/testingPrompts/<EndpointName>.md`. **Write those
prompt files.** (They sit beside the review because they're generated from it and
are disposable — the `*.Tests` projects hold the real HTTP/integration tests.) Each must be self-contained for a context-less agent: endpoint path,
file location, request/response shape, scenarios (happy + edges + auth + IDOR),
fixtures/helpers to reuse, and KnownGap-pinning conventions — so the recipient
doesn't have to rediscover the test infrastructure.

---

# FILE 2 — `review/<module>/02-findings.md`

The "what's wrong now" file. Every finding: **ID · severity · `file:line` · why it
matters · suggested fix · `Doc impact:` line.** Group into the sections below. Do
not restate previously-fixed or KnownGap items except to note a regression or
partial fix.

### 5. Security & authorization
Role checks, user-scoping leaks, IDOR on `{id}` endpoints, mass-assignment,
sensitive data in audit/logs, concurrency (`row_version`), timezone/DST where
relevant, race conditions. → `SEC-n`.

### 6. Code quality
SOLID, testability, naming, layering, DTO/entity boundaries, async hygiene,
EF Core pitfalls (N+1, tracking, `ExecuteUpdate` vs audit interceptor), error
handling, Result-pattern usage, and the CLAUDE.md convention checklist above —
including **refactor opportunities** where a base class would replace custom code.
→ `CQ-n`.

### 9. Performance hotspots
Per-entity loops, in-memory aggregation that should be SQL, missing indexes,
synchronous heavy work on the request thread (e.g. materialized-view refresh),
query shapes that scale poorly. Table: `Where | Issue | Impact | Mitigation`, each
with a `PERF-n`. Include an **"indexes that should exist"** subsection. → `PERF-n`.

### Code/doc drift
Where the code contradicts `domain-map.md` (an invariant/rule the docs assert but
the code no longer honors, a navigation-index row that's wrong or missing, a
business rule that's changed). For each, say which side is wrong (fix code vs fix
doc vs both) and set the `Doc impact:` line accordingly. → `DOC-n`.

---

# FILE 3 — `review/<module>/03-risks-rollout.md`

Synthesis + forward-looking. References IDs from file 02 — never restates them.

### 11. Risk-ranked issue list
The master table, sorted by severity: `# | Sev | Issue (one line) | ID | File`.
This is the executive summary — every 🔴/🟠 from file 02 appears here. The IDs link
back to the detail.

### 10. Migration / rollout risks
Table: `Risk | Likelihood | Mitigation`. Cover schema changes implied by the
findings (new constraints on live data with possible dupes, column-type changes
that lock tables, enum/seed splits needing data migration), and any **drift risk**
(e.g. sandbox/duplicate copies of the module that must be kept in sync). → `MIG-n`.

### Audit-log gaps
Bulk ops (`ExecuteUpdateAsync`/`ExecuteDeleteAsync`) that bypass the interceptor,
or missing `[AuditIgnore]`/`[NoAudit]`/`IAuditService.LogAsync` calls. → `AUDIT-n`.

---

## Questions before you start (skip any with an obvious answer; otherwise ask)
1. Docs — do `summary.md` / `domain-map.md` exist for this module? If not, run the
   doc pass first, or proceed undocumented?
2. Scope — just `$ARGUMENTS`, or include its adjacent dependency modules?
3. Compliance depth — strict spec citation, or common-sense + flag gaps? (omit if
   no spec)
4. Coverage — inspection-only, or run `dotnet test /p:CollectCoverage=true`?
5. Severity threshold — include 🟡 nits inline, or Medium-and-above + a Nits appendix?
6. Off-limits — files/folders to skip, or known-broken areas to ignore?

## Defaults if I just say "go"
Consume existing module docs (run the doc pass first if absent); module + named
deps only; common-sense compliance with citations where confident; endpoint +
service coverage by inspection; 🟠-and-above inline with a short 🟡 Nits appendix;
single deep-read pass; three files under `review/<module>/` plus per-gap prompts
under `review/<module>/testingPrompts/`; top 5 risks summarized in chat.
