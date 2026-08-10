# Legal & Product Audit — $ARGUMENTS

> **`$ARGUMENTS`** = the module under audit (e.g. `Attendance`, `CestovneNahrady`). If empty, ask
> which module to audit. This is NOT `/module-review` (code review) — this command answers three
> owner questions: **(1) does the module satisfy current Slovak law, (2) is it complete for the
> target segment, (3) what would make it better** — features, business logic, and bigger
> module-sized product ideas. Code quality is out of scope except where it is load-bearing for a
> legal finding.

Reference example of the expected output quality:
`MojaDigitalnaFirma.Core.Attendance/docs/attendance-review-2026-07.md` (found the 2024/2026
holiday-law drift + a §112 gap that every pinned figure check had missed).

## Inputs (read in this order)

1. The module's `docs/summary.md` + `docs/domain-map.md` — orient, don't rebuild.
2. The module's law matrix if it has one (`docs/slovakLawTracabilityMatrix.md` or similar).
3. Root `docs/routineLawCheckups.md` — which of this module's figures are already registered.
4. Root `docs/modules.md` + the deployment-model context: **single-node modular monolith for
   Slovak companies under ~100 employees** — that segment defines "complete".
5. Prior audit/review files (`docs/*review*.md`, `review/`) and `[Trait("Status","KnownGap")]`
   tests — this is a delta audit; do not re-flag what is already known/accepted. List those as
   "accepted gaps" with one line of agreement or dissent.

## Working method

**Phase A — verify the law claims (docs lie by getting stale, not by being wrong).**

- Extract every legal claim the module makes: § references, seeded statutory figures,
  effective-dated rate rows, calendar/catalog data, quotas, thresholds. For each, open the
  *enforcing code* and confirm the claim is real (matrix says ✅ ≠ code does it).
- **Check the law moved under the code.** For every date-sensitive or CPI/politics-sensitive item
  (holiday catalogs, rates, caps, deadlines), verify against **current** law with WebSearch —
  cross-check **at least 2 independent sources**, prefer slov-lex/ministry pages, and cite them as
  markdown links in the findings. Today's date matters; your training knowledge is a hypothesis,
  not a source.
- **Audit the registry's blind spots.** `routineLawCheckups.md` only protects what it pins. Ask:
  which legal data does this module hard-code that is NOT registered there? (The Attendance audit's
  biggest finding was exactly the one unpinned item.) Every such item is either a new registry
  entry or a finding.
- **Hunt unmodeled law.** Walk the statute neighborhood of what the module claims to own (for a
  leave feature: the §§ around dovolenka — pro-rata, krátenie, sviatok interaction, payout). For
  each neighboring rule: modeled / consciously out of scope (documented where?) / silent gap.
  Silent gaps become findings; conscious ones get a matrix row so the lawyer-facing doc stays honest.

**Phase B — segment fit.** What does a <100-employee Slovak company expect from this domain that
the module doesn't do? Rank by expected customer demand. Explicitly list what you'd deliberately
NOT build (and why) — scope discipline is part of the deliverable.

**Phase C — bigger ideas.** Module-sized or cross-module opportunities: unimplemented seams that
already exist (reserved `ExportKind`s, Kernel contracts with no producer, registries with one
adapter), state-body integrations (Sociálna poisťovňa, Finančná správa, …), and orchestration
across existing modules. Each idea: what it is, why it fits the architecture, main risk, effort.

**Severity + effort conventions:**
- **HIGH** = the system silently computes a legally wrong number (money, balance, entitlement).
  **MEDIUM** = legal obligation unsupported, manual workaround exists. **LOW** = evidence/reporting
  gap or conditional on customer profile.
- Effort unit = **phases**: 1 phase ≈ one focused build session with tests + docs (the unit the
  `prompts/<module>/` sets use). Calibrate against completed builds (Integrácie phase, Majetok-scale
  module), never wall-clock time.

## Output

**One file:** `<ModuleProject>/docs/<module>-review-YYYY-MM.md` (kebab module name, current year-month):

- **Verdict in one paragraph** — first, answering all three owner questions.
- **Part 1 — Legal findings.** `L1..Ln`, severity-ordered. Each: the rule (§ + zákon), what the
  code does today (with `file:line` traces), consequence, fix direction (prefer no-migration
  modeling where possible — e.g. string-stored enum values), and **sources as links**.
- **Part 2 — Segment fit.** Ranked feature gaps + the deliberate non-goals.
- **Part 3 — Code-level notes.** Only load-bearing items; keep short.
- **Part 4 — Open questions for the owner.** The scoping forks (customer-profile questions,
  accepted-constraint questions) + a priority recommendation. Number them Q1..Qn.
- **Part 5 — Bigger product ideas.** `B1..Bn`, each with fit/risk/effort, closing with a
  **recommended-order + effort table** (order, phases, main risk per item).

**Chat output:** TLDR verdict, the HIGH findings with one-line consequences, the Part 4 questions.
Nothing else — the file is the deliverable.

**Rules of engagement:**
- The deliverable is the **assessment**. Do NOT implement fixes in the audit pass — wait for the
  owner's answer to Part 4 (they may approve inline, as "Q4: yes implement now").
- If fixes are later approved: matrix rows (✅ for fixed, 🟠 for confirmed-open), new
  `routineLawCheckups.md` entries for any drifting figure discovered, module docs
  (summary gotchas / domain-map rules / testing.md), and a status-update header block in the
  review file recording what was implemented.
- Save the findings summary + owner's roadmap answers to auto-memory (one `project` memory per
  module audit, linked from the index) so future sessions inherit the ranking without re-deriving.
