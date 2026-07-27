# Routine Law Checkups — automation registry

**Purpose.** A machine-checkable registry of the **numeric Slovak-law figures that are pinned in code** and
that **drift over time**, so a scheduled workflow (n8n, a cron agent, …) can routinely re-check each one
against its authoritative source and raise a PR/issue when a figure changes.

**This is not the legal-audit doc.** For "which rule is enforced and where" — the qualitative compliance
posture a lawyer reviews — see [`MojaDigitalnaFirma.Core.Attendance/docs/slovakLawTracabilityMatrix.md`](../MojaDigitalnaFirma.Core.Attendance/docs/slovakLawTracabilityMatrix.md).
That matrix maps *rules → enforcement*; **this file** is the narrower list of *numbers → source + code pin +
check cadence*. Keep them separate: the matrix is for humans, this is for the workflow.

## How figures are pinned (and why the workflow can't just mutate them)

Every figure below lives in **code** — a constant or a seeded **effective-dated** row — **not** in runtime
config. That is deliberate: the values are auditable, versioned in git, and (for rates) effective-dated so a
month spanning a change computes correctly per day. So the automation's job is **detect + propose**, never
live-mutate:

1. Fetch the authoritative source on a cadence.
2. Extract the current figure (+ the oznámenie/zákon number for rates).
3. Compare against the value pinned at the code location.
4. On a mismatch, open a PR/issue. For **effective-dated rates, add a NEW seeded row — never edit the old
   one** (history must survive); for **constants/quotas**, bump the constant and add a test.

Each figure has a **stable ID** (the workflow keys on it). Line numbers are hints only — match on the
file + symbol name, which is stable.

---

## Tier 1 — Monetary rate data (drifts on CPI; check monthly/quarterly)

These move whenever the Štatistický úrad price index crosses the threshold — roughly 1–2× per year, on no
fixed calendar. **Highest checkup priority.**

### `STRAVNE-BAND-5-12H` — stravné, 5–12 h time band (§152 ZP basis)

| | |
|---|---|
| **Current values** | `8,80 €` from 1.4.2025 (oznámenie **39/2025 Z. z.**); `9,30 €` from 1.12.2025 (oznámenie **280/2025 Z. z.**) |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/infrastructure/persistence/seeders/default/MealRateSeeder.cs` → `Seed()`, the seeded `MealRate` rows (`StravneBand5to12Eur` + `LegalRef`) |
| **Read by** | `MonthlyMealEntitlementService` ("rate valid on date X") — the §152 meal-contribution compute |
| **Authoritative source** | MPSVR SR cestovné náhrady: <https://www.employment.gov.sk/sk/praca-zamestnanost/vztah-zamestnanca-zamestnavatela/cestovne-nahrady/> (Určenie súm náhrad — §8); the oznámenie itself in the Zbierka zákonov: <https://www.slov-lex.sk/> (search "o sumách stravného") |
| **Legal basis** | §8 zák. 283/2002 Z. z. o cestovných náhradách; entitlement §152 ZP |
| **Cadence** | Monthly (the change is CPI-triggered, not calendar-fixed) |
| **Update action** | Add a new effective-dated `MealRate` row (seeder is idempotent; no migration needed). Recompute the three derived figures below from the new band. Both deployments seed via the same `MealRateSeeder`. |

### `MEAL-DERIVED` — figures derived from the band (same `MealRate` row)

All three are **stored** alongside the band (not recomputed in app code), so the checkup must update them
together when the band changes. Current ↔ formula:

| Field | = | 8,80 € band | 9,30 € band |
|---|---|---|---|
| `EmployerMaxContributionEur` (non-taxable ceiling) | 55 % of band | `4,84 €` | `5,12 €` |
| `MinVoucherValueEur` | 75 % of band | `6,60 €` | `6,98 €` |
| `MinFinancialContributionEur` | 55 % of min-voucher | `3,63 €` | `3,84 €` |

- **Code pin:** same `MealRateSeeder.cs` row.
- **`EmployerMaxContributionEur` doubles as the §5 ods. 7 písm. b) zákona 595/2003 tax-exempt ceiling** used
  by `MonthlyMealEntitlementService` for the taxable/non-taxable split — so this one figure is both the
  employer max **and** the tax limit. Source for the tax rule: zák. 595/2003 on <https://www.slov-lex.sk/>.

### `MIN-WAGE` — §120 ZP minimum wage claims (minimálne mzdové nároky), degrees 1-6

| | |
|---|---|
| **Current values** | From 1.1.2026: degree 1 `915 €`/mes. (`5,259 €`/h), 2 `1 031 €` (`5,925 €`/h), 3 `1 147 €` (`6,592 €`/h), 4 `1 263 €` (`7,259 €`/h), 5 `1 379 €` (`7,925 €`/h), 6 `1 495 €` (`8,592 €`/h) — coefficients 1.0-2.0 of the base, at an established 40h/week |
| **Code pin** | `MojaDigitalnaFirma.Core.EmployeeModule/infrastructure/persistence/seeder/MinWageRateSeeder.cs` → `Seed()`, the seeded `MinWageRate` rows (`MonthlyAmount` / `HourlyAmount` / `LegalRef` per `Degree`) |
| **Read by** | `EmployeeSalaryChangeWriter.EvaluateMinWageFloorAsync` (the §120 salary-write guard) — used by `ChangeEmployeeSalaryEndpoint`, `BulkSalaryAdjustmentEndpoint`, the internal-transfer finish, re-hire, and new-employee onboarding start |
| **Authoritative source** | MPSVR SR: <https://www.employment.gov.sk/sk/praca-zamestnanost/vztah-zamestnanca-zamestnavatela/odmenovanie/minimalne-mzdove-naroky/sadzby-od-1-januara-2015.html>; podnikajte.sk: <https://www.podnikajte.sk/pracovne-pravo-bozp/minimalna-mzda-podla-stupnov-narocnosti-v-roku-2026-minimalne-mzdove-naroky> |
| **Legal basis** | §120 ZP (stupeň náročnosti práce 1-6, §120 ods. 2; pro-rating by agreed weekly hours, §120 ods. 4) |
| **Cadence** | **Yearly, every January** — the base minimum wage is now set by a 60 %-of-average-wage automat, so it moves every 1.1. |
| **Update action** | Add new effective-dated `MinWageRate` rows for all 6 degrees (seeder is idempotent; no migration needed — same table, new rows). Never edit an existing row. |
| **⚠️ Note** | 2026 figures verified against the two sources above as of 2026-07-19 — **verify with právnik** before relying on them for payroll, per this file's own pinning convention. |

---

## Tier 2 — Quotas & structural caps (change only on a ZP / zákon amendment; check on legislation change)

These don't drift with CPI; they change only when the law is amended. **Lower priority** — watch the cited
zákon/§ on slov-lex for amendments (e.g. annual) rather than polling a price index.

### `PUBLIC-HOLIDAYS` — day-of-rest set (zák. 241/1993)

| | |
|---|---|
| **Current** | 15 catalog days per year; **working (no §122 surcharge, counts as working day):** 1.9. since 2024 (zák. 530/2023); 17.11. **permanent** + 8.5. and 15.9. **temporary/consolidation-linked** from 2026 (2025 consolidation package). All other days remain days of rest. |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/infrastructure/persistence/seeder/default/PublicHolidaySeeder.cs` → `BuildCatalogForYear(int year)` (the `year >= …` conditions selecting `PublicHolidayType.WorkingHoliday`) |
| **Read by** | `WorkLogComputation` / `EditWorkLogEndpoint` (§122 surcharge), `LeaveCalculationService.GetLeaveDaySpanAsync` (§112 vacation counting) — both key off `HolidayType != WorkingHoliday` |
| **Source** | zák. 241/1993 Z. z. on <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1993/241/> |
| **Cadence** | **Yearly (each autumn, before seeding the next year)** — the 8.5./15.9. suspension is explicitly temporary and may be lifted; new one-off changes have now happened twice in three years |
| **Update action** | Adjust the `year >= …` conditions (or add per-year rules) in `BuildCatalogForYear`; the startup `ReconcileHolidayTypesAsync` pass repairs already-seeded years automatically. Stored `HolidayHours` on past work logs are NOT recomputed. |
| **History note** | This was the registry's blind spot: the July 2026 review found the catalog stale (1.9. still seeded as a day of rest 2024–2026) while every figure listed here held. |

### `VACATION-DAYS` — annual leave (§103 ZP)

| | |
|---|---|
| **Current** | `20` days base; `25` days for age ≥ 33 at year-end **or** permanent child care |
| **Code pin** | `MojaDigitalnaFirma.Core.EmployeeModule/domain/model/entity/employee/Employee.cs` → `VacationDaysForYear(int year)` (~l.106–113). Mirrored as `[20, 25]` in `StatutoryYearlyBalanceProvider.Map[Dovolenka]` |
| **Source** | §103 ZP — <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2001/311/> |
| **Note** | §103 special categories (8 weeks — teachers/healthcare) intentionally **not modeled** (see the matrix) |

### `OVERTIME-CAP` — annual overtime (§97 ZP)

| | |
|---|---|
| **Current** | statutory `150` h/yr; max `400` h with written agreement (so an agreement may add ≤ `250` h) |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/application/utility/WorkLogComplianceChecker.cs` → `StatutoryAnnualOvertimeCap` (l.28), `MaxAnnualOvertimeCap` (l.31), `MaxAdditionalOvertimeHours` (l.34); `MojaDigitalnaFirma.Core.EmployeeModule/domain/model/entity/employee/Employee.cs:52` → `Employee.AnnualOvertimeCap` default (`150` h) — a duplicate of the same statutory figure, must bump together with the Attendance constants |
| **Source** | §97 ZP |

### `BOZP-TRAINING-INTERVAL` — periodic BOZP training interval (§7 ods. 5 z. 124/2006 Z. z.)

| | |
|---|---|
| **Current** | `36` months (statutory floor since the 1.1.2023 novela; previously 24 months) |
| **Code pin** | **Doc-level pin only — no code literal.** `MojaDigitalnaFirma.Core.EmployeeModule/docs/summary.md` (acknowledgments slice paragraph) and `docs/domain-map.md` (BOZP oboznamovanie bullet) state the figure in prose; the demo seed `MojaDigitalnaFirma.Core.EmployeeModule/infrastructure/persistence/seeder/DevPolicyAcknowledgmentSeeder.cs:58` (`ReacknowledgmentIntervalMonths = 24`) is a deliberately stricter demo default, not the statutory value itself — no change needed there on a statutory bump |
| **Source** | z. 124/2006 Z. z. §7 ods. 5 on <https://www.slov-lex.sk/> |
| **Cadence** | On legislation change |
| **Update action** | Update the "Current" value here and the two doc passages; the seeder's `24` stays unless the owner chooses to raise the demo default too |

### `PAY-TRANSPARENCY` — equal-pay / pay-transparency duties (zákon o rovnakom odmeňovaní, in force 7.6.2026)

| | |
|---|---|
| **Current** | Act **in force since 7.6.2026** (transposes directive (EÚ) 2023/970), binding on **all employers regardless of size**. Duties modeled: gender-neutral **pay criteria** in place by **30.6.2026**; the employee's **right to written information** on their own pay level + the average pay levels **by sex** for their category of workers. Answer deadline: **2 months** per the directive's Art. 7(4) — ⚠️ **the transposed figure is unverified**; it is deliberately not printed in the generated document. Gender-pay-gap **reporting** thresholds (250+ → 150+ → 100+ employees) are **out of segment and not built**. Disclosure floor pinned in code: `EqualPayMinGroupSize = 3` (a product/GDPR decision — the act sets **no** minimum cell size). |
| **Code pin** | `MojaDigitalnaFirma.Core.EmployeeModule/domain/constant/EqualPayConstants.cs` → `EqualPayMinGroupSize`. Everything else is **doc-level**: `MojaDigitalnaFirma.Core.EmployeeModule/docs/summary.md` (pay-transparency bullet) + `docs/domain-map.md` (pay-transparency business rule + the `Pay transparency / equal pay` navigation section) state the dates, the deadline and the `JobTitle` equal-value proxy in prose |
| **Read by** | `EqualPayCalculator` (the single suppression/aggregation path) → `EqualPayReportEndpoint` (`GET employee/equal-pay-report`) and `GenerateEqualPayInfoEndpoint` (`POST employee/{id}/equal-pay-info`); the pay-criteria evidence slot is `AcknowledgmentCategory.PayCriteria` |
| **Authoritative source** | [Škubla & Partneri](https://www.skubla.sk/clanky/navrh-zakona-o-rovnakom-odmenovani-slovensko-zacalo-transpoziciu-smernice-o-transparentnom-odmenovani); [Grant Thornton](https://www.grantthornton.sk/novinky/transparentne-odmenovanie-2026); the act itself + any vykonávacie predpisy on <https://www.slov-lex.sk/>; directive (EÚ) 2023/970: <https://eur-lex.europa.eu/legal-content/SK/TXT/?uri=CELEX:32023L0970> |
| **Legal basis** | zákon o rovnakom odmeňovaní mužov a žien za rovnakú prácu alebo za prácu rovnakej hodnoty; smernica (EÚ) 2023/970 (esp. Art. 7 — right to information). Suppression rationale: GDPR čl. 5 ods. 1 písm. c) |
| **Cadence** | **On legislation change.** The act is new, so **vykonávacie predpisy / metodika may follow** and would be the first thing to move — worth a look each quarter until one lands, then per-amendment |
| **Update action** | Confirm the response deadline and any prescribed grouping/format for the information answer; if a minimum cell size is ever prescribed, bump `EqualPayMinGroupSize` (+ its XML-doc) rather than the call sites. If the reporting thresholds ever reach the <100-employee segment, that is a **new build**, not a figure bump |
| **⚠️ Note** | Two open lawyer questions carried in code as `⚠️ verify with právnik`: (1) **`JobTitle` is the v1 proxy for "work of equal value"** — a finer grouping plugs into `EqualPayCalculator.LoadActiveSalariesAsync`'s group key; (2) the **Slovak wording** of the generated document's `Note` / legal-basis blocks (`GenerateEqualPayInfoEndpoint.BuildNote`, `GenerateEqualPayInfoCommandHandler`) |

### `DOHODY-CAPS` — dohody mimo pracovného pomeru: hour caps, duration, notice (§223–§228a ZP)

| | |
|---|---|
| **Current** | **DoVP** (§226): `350` h per **calendar year** per employer. **DoPČ** (§228a): a **strict** `10` h **per week** — *not* an average and *not* a 520 h/year budget. **DoPČ na sezónnu prácu** (§228a): `520` h per calendar year, agreement max `8` months (its average weekly time may reach 40 h over ≤ 4 months). **DoBPŠ** (§227–§228): `20` h per week **on average**, averaged over the whole agreed period (≤ 12 months). **Duration** (§223): a dohoda is concluded for a definite period of at most `12` months. **Skončenie** (§228a): výpoveď **without any reason**, `15`-day notice running from **delivery**; **a DoVP cannot be ended by výpoveď at all** (§226) — it ends by performance of the task or lapse of the period. **No skúšobná doba** on any dohoda (§45 is a pracovný pomer institute). |
| **Code pin** | `MojaDigitalnaFirma.Core.EmployeeModule.Contracts/domain/model/DohodaLimits.cs` — **one pin for both modules**: `MaxDoVPHoursPerYear`, `MaxDoPCHoursPerWeek`, `MaxSeasonalDoPCHoursPerYear`, `MaxSeasonalDoPCDurationMonths`, `MaxDoBPSAverageHoursPerWeek`, `MaxDurationMonths`, `NoticePeriodDays`, `DoVPHasNoNotice`. The regime itself is `AgreementKind` (same folder), stored on `EmploymentType.AgreementKind`. |
| **Read by** | **Employee:** `EmploymentTermsValidator.ValidateDohodaTerms` (onboarding start/finish, re-hire) and `.ValidateEmploymentTypeChangeAsync` (**every path that sets `EmploymentTypeId`** — the employee PUT, the transfer wizard, the CSV bulk import), `DohodaTerminationValidator` (the termination wizard's end-mode branch), plus the outright refusals in `RenewContractEndpoint` and `EndDuringProbationEndpoint`. **Attendance:** `WorkLogComplianceChecker.EvaluateDohodaCapViolations` — shared by the write-time `CheckDohodaHourCapAsync` (every work-log write path) and the read-only `ComplianceViolationsService` (`ComplianceRule.DohodaHourCap`), so the gate and the report cannot disagree. The kind crosses the module boundary **only** through the `GetEmployeeWorkScheduleCommand` Contracts seam (`EmployeeWorkSchedule.AgreementKind` / `AgreementStart` / `AgreementEnd`). |
| **Authoritative source** | MPSVR SR: <https://www.employment.gov.sk/sk/praca-zamestnanost/vztah-zamestnanca-zamestnavatela/zakonnik-prace/>; podnikajte.sk: <https://www.podnikajte.sk/pracovne-pravo-bozp/dohoda-o-vykonani-prace-o-pracovnej-cinnosti-2026> and <https://www.podnikajte.sk/pracovne-pravo-bozp/kolko-hodin-mozno-odpracovat-na-dohodu>; the ZP itself on <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2001/311/> |
| **Legal basis** | §223 (spoločné ustanovenia, duration), §226 (DoVP), §227–§228 (DoBPŠ), §228a (DoPČ + sezónna práca + skončenie) ZP |
| **Cadence** | **On legislation change** — but check more eagerly than the other Tier-2 entries: the dohody figures and the odvody regime around them have moved **several times in the 2020s** (the seasonal DoPČ is itself a recent addition), so they drift more than a typical structural cap. |
| **Update action** | Bump the constant in `DohodaLimits` (+ its XML-doc) and add a test — never restate a figure at a call site. A **new dohoda type** means a new `AgreementKind` value (string-persisted, so no migration for the enum), a `DohodaLimits` entry, an arm in `CheckDohodaHourCapAsync`, and a seeder row. |
| **⚠️ Note** | Figures verified 2026-07-20 against the sources above — **verify with právnik** before relying on them. Two known simplifications carried in code: (1) the **520 h seasonal cap is enforced yearly** but the "40 h average over ≤ 4 months" sub-rule is **not** modeled; (2) the generated dohoda contract / termination paperwork falls back to the pracovný pomer templates unless `WordTemplate:DohodaContract` / `WordTemplate:DohodaTermination` are configured — **the fallback wording is wrong for a dohodár and must be verified with a právnik**. |

### `REST-BREAK` — rest & break thresholds (§91/§92/§93 ZP)

| | |
|---|---|
| **Current** | daily rest ≥ `12` h; weekly rest proxied as ≤ `6` consecutive work days; mandatory `30`-min break after `> 6` h worked |
| **Code pin** | `WorkLogComplianceChecker.cs` → `MinDailyRestHours` (l.16), `MaxConsecutiveWorkDays` (l.19), `MaxHoursBeforeMandatoryBreak` (l.22), `MinBreakMinutes` (l.25) |
| **Source** | §91 / §92 / §93 ZP |

### `NIGHT-WINDOW` — night-work window (§123 ZP)

| | |
|---|---|
| **Current** | `22:00 – 06:00` |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/application/service/WorkLogSurchargeCalculatorService.cs` (the `day.AddHours(6)` / `day.AddHours(22)` sub-window split, ~l.26–36) |
| **Source** | §123 ZP (Saturday/Sunday/holiday surcharge windows §122/§122a are structural, same service) |

### `MAX-SHIFT` — maximum shift length (§85 ZP)

| | |
|---|---|
| **Current** | `12` h incl. overtime |
| **Code pin** | `WorkLogRequestValidator` (the shift-length rule) |
| **Source** | §85 ZP |

### `MEAL-THRESHOLD-4H` — meal entitlement threshold (§152 ods. 1 ZP)

| | |
|---|---|
| **Current** | a shift worked **> 4 h** earns one meal contribution |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/application/service/MonthlyMealEntitlementService.cs` → `EntitlementHoursThreshold` |
| **Source** | §152 ods. 1 ZP |

### `STATUTORY-LEAVE-QUOTAS` — per-type leave maxima (ZP §141/§166, zák. 461/2003 §34/§39/§39a)

| | |
|---|---|
| **Current** | doctor visit `7` d/yr (§141); bereavement `2` d/case (§141); DPN `52` weeks, employer pays first `10` days (§34 z.461/2003 + §144a ZP); OČR `14`/`10` d/case, dlhodobé `90` d (§39/§39a); materská `34/37/43` týž., otcovská `28` týž., otcovské voľno `2` týž. (§166 ZP) |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/application/service/StatutoryYearlyBalanceProvider.cs` → `Map` (l.45–84); `LeaveType.MaxLength` in `LeaveTypeSeeder` |
| **Source** | ZP §141/§166 + zák. 461/2003 (§34/§39/§39a) on slov-lex |
| **Note** | DPN day-band **rates** (1–3 @ 25 %, 4–10 @ 55 % DVZ) are payroll's job — not pinned here (see the matrix) |

### `WORKHOUR-ROUNDING` — duration rounding convention

| | |
|---|---|
| **Current** | work logs round **down** to the quarter-hour; leave per-day overlap rounds **up** to the half-hour |
| **Code pin** | `MojaDigitalnaFirma.Core.Attendance/application/service/WorkHourCalculatorService.cs` |
| **Source** | operational convention (not a single §); change only on legal/owner direction |

### `GDPR-CONSTANTS` — GDPR/ZoOOÚ statutory figures (Art. 12(3), Art. 33(1), Art. 30(5))

| | |
|---|---|
| **Current** | DSAR deadline `1` calendar month (+`2` months extension); breach → ÚOOÚ `72` h from awareness; RoPA exemption threshold `250` persons (informational) |
| **Code pin** | `MojaDigitalnaFirma.Core.OchranaUdajov/domain/constant/GdprConstants.cs` → `DsarResponseMonths`, `DsarExtensionMonths`, `BreachAuthorityNotificationHours`, `RopaEmployeeExemptionThreshold` (each with an XML-doc citing Article + § + effective date) |
| **Read by** | `DataSubjectRequestService` (deadline + extension), `DataBreachService` (72h clock), RoPA docs |
| **Source** | GDPR consolidated: <https://eur-lex.europa.eu/legal-content/SK/TXT/?uri=CELEX:02016R0679>; ZoOOÚ: <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2018/18/> |
| **Cadence** | On legislation change only — these are EU-level figures a national amendment cannot move; the realistic drift is **citation renumbering** (see `GDPR-ZOOOU-ALIVE`) |
| **Update action** | Bump the constant + its XML-doc effective date, update the module's `docs/lawTracabilityMatrix.md` row + Review log |

### `GDPR-ZOOOU-ALIVE` — is zákon č. 18/2018 Z. z. still the law the module cites?

| | |
|---|---|
| **Current** | 18/2018 **in force** (verified 2026-07-07). ⚠️ **Dated:** a promulgated novela takes effect **18.08.2026** (amending act to identify — see the module review Q6/prompt); a **full replacement** (LP/2025/305 + LP/2025/306) is in the legislative pipeline and will renumber every § cite |
| **Code pin** | Not a number — the § citations in `GdprConstants.cs` XML-docs + `MojaDigitalnaFirma.Core.OchranaUdajov/docs/lawTracabilityMatrix.md` (§13–16, §29, §34, §37, §40/41, §42, §79) |
| **Source** | Slov-Lex version list: <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2018/18/>; legislative process: <https://www.slov-lex.sk/legislativne-procesy/SK/LP/2025/305>; ÚOOÚ aktuality: <https://dataprotection.gov.sk/> |
| **Cadence** | Monthly until the replacement law is resolved, then per-amendment |
| **Update action** | On a new version/adoption → run the module's re-verify pass (`MojaDigitalnaFirma.Core.OchranaUdajov/docs/ochrana-udajov-review-2026-07.md` L2/L3; readiness prompt in `prompts/gdpr-followups/`) |

### `RETENTION-EMPLOYEE-ERASURE` — employee auto-anonymization window + evidence carve-out

| | |
|---|---|
| **Current** | `RetentionYears` default **10** — **options-bound** (`EmployeeRetention:RetentionYears`), floor-validated **≥ 10** at startup (a shorter window cannot be configured). Documents in the **employment-evidence class** (`Contract`, `Amendment`, `TerminationAgreement`) are **excluded from the purge entirely** and currently retained indefinitely. |
| **Code pin** | `MojaDigitalnaFirma.Core.EmployeeModule/application/job/EmployeeRetentionOptions.cs` → `DefaultRetentionYears` / `MinimumRetentionYears`; the job `application/job/AnonymizeTerminatedEmployeesJobHandler.cs`; the carve-out `domain/model/enum/EmployeeDocumentCategory.cs` → `IsEmploymentEvidence` + `application/service/EmployeeErasureService.cs` → `EraseVaultDocumentsAsync`. Validation registered in `MojaDigitalnaFirma.AdminPortal/config/CoreServiceExtensions.cs` (`ValidateOnStart`) |
| **Read by** | `AnonymizeTerminatedEmployeesJobHandler` (nightly, retention-driven) and `AnonymizeEmployeeEndpoint` (manual Art. 17) — both share `EmployeeErasureService`, so the carve-out applies to both paths |
| **Source** | §231-family zák. č. **461/2003** Z. z. (employer social-insurance evidence duty) + §35 ods. 3 zák. č. **431/2002** Z. z. o účtovníctve, on <https://www.slov-lex.sk/>; registratúra practice: [pracovnepravo.sk — Spôsob a forma uchovávania mzdovej účtovnej dokumentácie](https://www.pracovnepravo.sk/clanky-personalistika/195/sposob-a-forma-uchovavania-mzdovej-uctovnej-dokumentacie) (mzdové listy 50 r., osobné spisy 70 r.) |
| **Cadence** | On legislation change **and** on the DPO's registratúrny-plán decision landing (whichever comes first) |
| **Update action** | Bump `DefaultRetentionYears` (+ `MinimumRetentionYears` if the statutory floor itself moves) and its XML-doc; **revisit the carve-out set** in `IsEmploymentEvidence` and replace the indefinite retention with the plan's real per-class clocks (the `TODO(DPO)` markers in `EmployeeDocumentCategory.cs` + `EmployeeErasureService.cs`) |
| **History note** | Until the July 2026 audit (finding L1) the figure was an unregistered hard-coded constant justified by **§35 z. 461/2003** — which is *"Vylúčenie nároku na výplatu nemocenského"* and has nothing to do with retention. ⚠️ **Verify the replacement citation with právnik/DPO**; the full retention-class split is still pending. |

### `CRZ-PUBLICATION-RULES` — povinné zverejňovanie: effectiveness offset + conclusion deadline

| | |
|---|---|
| **Current** | Účinnosť = publication **+ 1** day (floor; an agreed *later* účinnosť wins — §47a ods. 2); a povinne zverejňovaná zmluva not published within **3** months of `ConcludedOn` is deemed never concluded |
| **Code pin** | `MojaDigitalnaFirma.Core.Zmluvy/domain/ZmluvyConstants.cs` → `CrzEffectivenessOffsetDays`, `MandatoryPublicationDeadlineMonths` |
| **Read by** | `ContractEventService.ActivateAsync` + `ContractAmendmentService.AddAsync` (both take `max(agreed, published + offset)`), `ContractDeadlineCalculator` (`PublicationDeadline`), the CRZ report |
| **Source** | §47a OZ: <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1964/40/>; §5a z. 211/2000 Z. z.: <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2000/211/>; CRZ metodika: <https://www.crz.gov.sk/faq/> |
| **Cadence** | On legislation change |
| **Update action** | Bump the constant + its XML-doc, update the CRZ row of `MojaDigitalnaFirma.Core.Zmluvy/docs/domain-map.md` (business rules) and the module review log |

### `LEASE-NOTICE-116-1990` — výpovedná lehota for nájom nebytových priestorov

| | |
|---|---|
| **Current** | **3** months unless a longer one is agreed, counted **from the first day of the calendar month following delivery** of the výpoveď → the nájom ends on the last day of the n-th full month after the month of service. ⚠️ The *counting convention* is as load-bearing as the number — a change to either is a code change, not just a constant bump. §12's fixed-term reason catalog (§9) is consciously **not** enforced |
| **Code pin** | `MojaDigitalnaFirma.Core.Zmluvy/domain/ZmluvyConstants.cs` → `LeaseStatutoryNoticeMonths`; the convention in `application/service/ContractEventService.cs` → `LeaseNoticeEnd` (applied only for `ContractType.Lease`; other types keep §122 OZ counting from the day of service) |
| **Read by** | `ContractEventService.TerminateAsync` (`TerminationReason.Notice`) |
| **Source** | zák. č. 116/1990 Zb. §12 (§9 for the fixed-term reason catalog): <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1990/116/> |
| **Cadence** | On legislation change |
| **Update action** | Bump the constant **and re-check the month-start counting rule** in `LeaseNoticeEnd`; update the lease row of the module's `docs/domain-map.md` + the `Terminate_Lease_ByNotice_*` theory cases |

### `PAYMENT-TERMS-60D` — B2B splatnosť ceiling (§340a ObZ)

| | |
|---|---|
| **Current** | **60** days. A longer agreed term is **suspect, not invalid** (permissible if expressly agreed and not grossly unfair to the creditor) — therefore a **warning badge, never a validation block** |
| **Code pin** | `MojaDigitalnaFirma.Core.Zmluvy/domain/ZmluvyConstants.cs` → `StatutoryMaxPaymentTermsDays`; surfaced by the derived `application/dto/contract/ContractDto.cs` → `ExceedsStatutoryPaymentTerms` |
| **Read by** | `ContractDto` (grid + get-by-id badge). Deliberately **not** read by `ContractRequestValidator` |
| **Source** | §340a Obchodného zákonníka (zák. č. 513/1991 Zb.): <https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1991/513/> |
| **Cadence** | On legislation change |
| **Update action** | Bump the constant + its XML-doc; keep it a badge — if the ceiling ever becomes an absolute cap, that is a *new* decision (a validation rule + a migration story for existing rows), not a constant bump |

---

## Suggested n8n workflow shape

- **Tier 1 (monthly):** fetch the MPSVR stravné page (and/or the slov-lex Zbierka filtered to "o sumách
  stravného"); extract the latest 5–12 h amount + oznámenie number; compare to the newest `MealRate` row's
  `StravneBand5to12Eur`. On change → open a PR adding a new effective-dated `MealRate` row with the band, the
  three derived values (55 % / 75 % / 55 %-of-75 %), the `EffectiveFrom`, and a `LegalRef` citing the new
  oznámenie. The seeder is idempotent, so no migration is required.
- **Tier 2 (e.g. quarterly / on amendment alerts):** watch slov-lex for amendments to the cited zákony/§§
  (ZP 311/2001, z. 283/2002, z. 461/2003, z. 595/2003). On a hit → open an issue pointing at the code pin; a
  human bumps the constant + test.
- Key each check on the **stable ID** above so the workflow's state survives doc edits.

> Keep this file and the values it cites in sync with `MealRateSeeder.cs` and the constants it points to —
> when you change a pinned figure in code, update the "Current" value here in the same PR.
