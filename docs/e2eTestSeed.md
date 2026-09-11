# `POST /api/test/seed` — the e2e fixture reset

Resets the database to the one known state the SPA's Playwright suite expects. The SPA's
`e2e/global-setup.ts` calls it once before any spec and before any login, and throws if it cannot
reach it — so **no spec can run without this endpoint**, not even one that never touches seeded data.

This document is the operator- and client-facing half: how to switch it on, what it guarantees, and
what it destroys. The design rules (why the gate is a registration filter and not a role, why the
fixture is not a dev seeder) are in
[claude/endpoints.md](claude/endpoints.md#the-e2e-fixture-endpoint); the guards are in
[claude/testing.md](claude/testing.md#things-that-only-a-behavioural-test-catches).

> This file was written for **this** solution and is accurate. It is not one of the leftover
> MojaDigitalnaFirma copies CLAUDE.md warns about.

---

## ⚠ What it destroys

`TRUNCATE … RESTART IDENTITY CASCADE` over **every table in the EF model**, then a rebuild. That
includes the root admin account and every row any human has created in that database. It is a reset,
not an upsert, because the polluted-state problem it exists to solve is exactly "rows a previous run
left behind".

Two things it deliberately leaves alone:

- **`__EFMigrationsHistory`** — it is not an EF entity, so it cannot enter the model-derived table
  list. Migration state survives.
- **The `scheduler` schema** (`scheduled_job`, `scheduled_job_run`) — operational state, not fixture
  data. The registrars that repopulate it only run at boot, so truncating it would leave the app with
  no scheduled jobs until the next restart, silently.

**Point this at a database you are willing to lose.** The gate below is what stops you doing
otherwise by accident — and because its database half takes no configuration, there is nothing to
mis-configure your way past.

---

## Turning it on

The route only exists where **both** conditions hold. Neither is a flag that can be switched on by
copying an `appsettings` block into the wrong environment, and the second takes no configuration at
all.

### 1. The hosting environment

| | |
|---|---|
| Always allowed | `Development` |
| Allowed if named in `TestSeed:AllowedEnvironments` | anything else |
| **Never allowed, even if named** | `Production`, `Staging` |

### 2. The database must be on this machine

`DB_HOST` must be loopback or the Docker host alias:

```
localhost   127.0.0.0/8 (any of it, e.g. 127.0.0.1)   ::1   host.docker.internal
```

`host.docker.internal` is included because a containerised app reaching a Postgres on the developer's
own host is still a local database.

**`DB_PORT` and `DB_NAME` are not consulted at all**, and **there is no configuration key that admits
a remote host.** That is deliberate, not an omission — any such key is a way to point a truncating
endpoint at a shared database, and the entire value of this check is that it cannot be. If you want
e2e, run a local Postgres. `TestSeedGateTests.NoConfiguration_CanAdmitARemoteDatabase` pins that no
plausible spelling of such a key has quietly appeared.

A hostname that is neither a known local name nor a loopback IP literal is **refused, not resolved**.
DNS is deliberately not consulted: a name that resolves to loopback today can resolve elsewhere
tomorrow, and a gate whose answer depends on a lookup changes without the connection string changing.
So `db.internal`, `localhost.evil.example.com` and `my-localhost` are all closed.

### ⚠ Why it is the host, and not the port or the name

**This solution's dev and prod databases are both `main_db` on `187.77.77.42`, and differ only by
port** — 5443 for dev, 5444 for prod (see `AdhdTimeOrganizer/.env`). So:

- A check on the database **name** cannot tell them apart at all.
- A check on the **port** would admit any host that happened to serve on the allowed port, including
  a production one.
- A check on the **environment** alone is one edited `ASPNETCORE_ENVIRONMENT` away from truncating
  production.

"Is this database on the machine the app is running on" is the only question whose answer cannot be
got wrong by editing a connection string.

**Consequence for a stock checkout:** the dev `.env` points at that shared remote host, so the
endpoint **refuses by default, on both ports**. Point the app at a local Postgres to run e2e —
`DB_HOST=localhost` — there is no other way in.

> Run your local Postgres on whatever port you like and set `DB_PORT` to match — the gate does not
> look at the port. It used to have to be 5433: the integration-test suite starts its own Postgres via
> Testcontainers, and that container pinned host port 5432 until 2026-09-11, so a local Postgres on
> the default port failed the whole `Postgres` collection in `InitializeAsync`. The container now
> takes a free port of its own and the collision is gone.

### Fails closed

A blank environment name, an unparseable connection string, or a connection string naming no host all
close the gate. There is no path that opens it by omission.

### Confirming which way it went

One line at boot, every time:

```
[WRN] POST /api/test/seed IS ENABLED — environment 'Development', database host 'localhost'. It resets the database on every call.
[INF] POST /api/test/seed is not registered: the database host '187.77.77.42' is not this machine.
```

The reason carries the environment and the host only — never the database name, the user or the
password.

---

## The contract

```
POST /api/test/seed          → 200 once the database is in the fixture state
```

- **No authentication.** `globalSetup` runs with no cookie, and the accounts it needs are the ones
  this call creates. The protection is entirely "which process, which database", never "who is
  calling".
- **Synchronous.** It returns when the reset is complete. There is no 202-and-poll shape.
- **Any non-2xx fails the whole test run**, with the status and body surfaced. There is no
  try/catch — a partial fixture is worse than no fixture.
- **Cost:** ~1.5–2.0 s against a local Postgres.

### When the gate is closed

The route is **not registered** — filtered out of FastEndpoints discovery, so there is no endpoint to
authorize and none in Swagger. It is never 403 (which would confirm it is there) and never 200.

Note what "absent" actually looks like in this app: `IdentityServiceExtensions` sets an authorization
`FallbackPolicy` of `RequireAuthenticatedUser`, and ASP.NET applies the fallback to requests matching
**no** endpoint too. So every undefined path here answers **401** to an anonymous caller, not 404 —
and a closed gate is byte-for-byte indistinguishable from `/api/no-such-route`.

---

## Stable ids

Declared in `AdhdTimeOrganizer/infrastructure/testSupport/E2eFixtureIds.cs` and echoed in the response
body, so a spec may hardcode them or read them.

**These are a contract with the SPA repo. Add an id rather than renumbering an existing one.**

| What | Id(s) |
|---|---|
| primary account `test@example.com` | `10001` |
| secondary account `test2@example.com` | `10002` |
| activity roles (primary) | `11001` Work, `11002` Health & Fitness, `11003` Hobbies & Leisure |
| activity categories (primary) | `11501` Deep Work, `11502` Quick Tasks, `11503` Errands |
| activities | `12001`–`12022` |
| — archived | `12010` Retired Hobby |
| — referenced, so `canDelete: false` | `12011` Write Blog Post |
| — unreferenced, so `canDelete: true` | `12012` Unused Idea |
| to-do lists | `13001` Focus, `13002` Errands, `13003` Someday (**deliberately empty**) |
| items with a non-zero `suggestedTime` | `14001`–`14006` |
| overdue, not done | `14007`–`14012` |
| multi-count, 2 of 5, not done | `14013` |
| completed today (daily recap) | `14014` |
| temptation-bundled (paired leisure activity) | `14015` |
| activity history | `15001`–`15018` (calibration), `15101` |
| planner tasks | `16001`–`16007` |
| backlog / bucket-list / project profiles | `17001` / `17101` / `17201` |
| memory anchor | `18001` |
| routines (daily / weekly / monthly) | `19001` / `19002` / `19003` |
| secondary account's own data | `11101`, `11601`, `12101`–`12102`, `13101`, `14101`–`14102`, `15501` |

**Ids the fixture does not choose** — task priorities and routine time periods are created by the real
sign-up path (`IUserDefaultsService`), so they are **resolved and returned** in the response
(`taskPriorities`, `routineTimePeriods`) keyed by their English text rather than pinned here.

### Why the bands start at 10 000

The reset restarts every identity sequence, so auto-generated ids are already reproducible — but only
as long as the row counts ahead of them never change, and one of those counts is "every calendar day
in two years", which moves on a leap year. Explicit ids in a band above every auto-generated row make
the values a spec asserts on independent of that. Each touched sequence is then advanced past its
band, so a row the app creates *during* a spec cannot be handed a fixture id.

---

## What the fixture contains

Beyond a representative dataset, these are the specific surfaces that were fully built and simply
never rendered, because no data exercised them:

1. **The e2e account**, confirmed, signing in immediately, **locale `EN`** — `App.vue` drives
   `i18n.locale` off the persisted column, so a Slovak default breaks every label-based locator.
2. **A second account with data of its own**, for the per-account `localStorage` isolation checks.
3. **Six items with a non-zero `suggestedTime`**, each on an activity carrying **three**
   `ActivityHistory` rows (days −1, −3, −7). The actual durations straddle the estimate rather than
   matching it. One recording per item is linked back to the item (`TodoListItemId`), so item-level
   attribution is exercised alongside the activity average. Six, not five, so the header ratio line
   stays above its ≥5 threshold if a spec completes one mid-run.
4. **Six overdue, not-done items**, 1 / 3 / 5 / 7 / 10 / 14 days past due, each on a distinct date.
5. **A multi-count item** at `DoneCount 2` of `TotalCount 5`, not done.
6. **Planner data spanning a known range** — 3 tasks today, 2 tomorrow, **none on today+2**, 2 on
   today+3. The calendar rows exist for all four days: the empty day is empty of *tasks*, not missing.
7. **An archived activity** (really hidden by the archival query filter, not merely flagged), plus the
   `canDelete` pair above.
8. **One leisure profile of each kind** on distinct activities, plus a **memory anchor** on the
   bucket-list one. A bucket-list entry carries no `IsRepeatable` — that is the backlog profile's
   field — so it is inherently the one-time kind, which is what makes it the right one to anchor.

Plus three routines (daily / weekly / monthly) with streaks in three different states.

### Determinism

- **Dates are clock-relative**, computed from **one** UTC date read at the start of the reset, so two
  calls on the same day are identical and a call either side of midnight is honest about it. The date
  used is returned as `seedDateUtc`.
- **"Identical values" means everything a spec can observe.** Ids, names, counts, dates, due dates and
  display orders are identical across calls. Password-hash salts, `ModifiedTimestamp` and
  `CompletedTimestamp` are not, by construction. `TestSeedEndpointTests.Seed_Twice_LeavesIdenticalRows`
  snapshots every row out of the database, seeds again, and compares.

### ⚠ `testpass123` does not satisfy this app's own password policy

`IdentityServiceExtensions` requires 10 characters, an uppercase letter, a digit and a
non-alphanumeric, so `UserManager.CreateAsync` would reject it. The seeder writes the hash directly
and sign-in — which only verifies the hash — works. The consequence: **the e2e account cannot be
recreated through sign-up**, only through this endpoint.

### ⚠ The overdue-banner threshold lives in the SPA

This project cannot see it, so the fixture aims well above any plausible value rather than at a
specific one, and the response reports `counts.overdueTodoListItems` so a spec can assert on the real
number instead of assuming.

---

## Response body

```jsonc
{
  "fixture": "e2e-v1",
  "seedDateUtc": "2026-09-10",
  "primaryUser":   { "id": 10001, "email": "test@example.com",  "password": "testpass123", "locale": "EN" },
  "secondaryUser": { "id": 10002, "email": "test2@example.com", "password": "testpass123", "locale": "EN" },
  "activities":    { "bugFixing": 12001, "archived": 12010, "notDeletable": 12011, "deletable": 12012, /* … */ },
  "todoLists":     { "focus": 13001, "errands": 13002, "empty": 13003, "secondaryWork": 13101 },
  "todoListItems": { "withEstimate": 14002, "withEstimateAndSteps": 14001,
                     "multiCountPartial": 14013, "completedToday": 14014,
                     "temptationBundled": 14015, "mostOverdue": 14007, "leastOverdue": 14012 },
  "profiles":      { "backlog": 17001, "bucketList": 17101, "project": 17201, "memoryAnchor": 18001 },
  "routines":      { "daily": 19001, "weekly": 19002, "monthly": 19003 },
  "taskPriorities":     { "Today": 9, "This week": 10, "This month": 11, "This year": 12 },
  "routineTimePeriods": { "Daily": 5, "Weekly": 6, "Monthly": 7, "Yearly": 8 },
  "counts": {
    "activities": 22, "todoListItems": 15, "overdueTodoListItems": 6,
    "calibrationTodoListItems": 6, "activityHistoryEntries": 19,
    "plannerTasks": 7, "calendars": 730
  }
}
```

The two lookup maps' values are illustrative — they are auto-generated, which is why they are returned
rather than documented as constants. Everything else is fixed.

The password is echoed back deliberately: this endpoint only exists where the account it describes is
a fixture, and a spec reading the credential from the response cannot drift from the seeder the way a
second copy in `.env.test` can.

---

## What belongs in the fixture — the admission rule

Every request to "add X to the seed" gets the same three questions, in order. Only the third is a
reason to touch `E2eFixtureSeeder.cs`.

1. **Can the spec just read what is already here?** The fixture is a shared, read-only baseline. Most
   additions are really a spec that has not looked.
2. **Can the app itself create X?** Then the spec creates it, through the real API, with a Playwright
   `request` context on the same storage state. This is *better* than seeding it: it exercises the
   write path you ship, the data is the spec's own so nothing else can disturb it, and the fixture
   does not grow. A new list, an item, an activity, a profile all fall here.
3. **Is X a state the app has no way to produce?** *Then* it belongs in the fixture. A due date in the
   past. Three `ActivityHistory` rows dated −1 / −3 / −7. A streak mid-run. A row behind the archival
   query filter. A second account. These are exactly the eight surfaces the fixture holds today, and
   the list is short on purpose.

**Do not make the endpoint take a shape parameter.** "Give me three overdue items and a weekly
streak" turns this into a second write API, parallel to the real one and untested by anything that
ships — and every spec then encodes its setup twice. A spec that needs its own world creates it
through rule 2; a spec that *mutates* fixture rows uses the SPA's `freshSeed` option
(`test.use({ freshSeed: true })`), which re-POSTs this same endpoint between tests. That is the whole
escape hatch, and it works because the reset is idempotent.

The reason the boundary matters: everything admitted under rule 3 is a row no spec can rebuild if it
gets clobbered, and with `workers: 1` and one reset per run, any spec that writes to a fixture row
changes the world for every spec after it.

---

## Changing the fixture

- Ids and the fixture version live in `E2eFixtureIds.cs`. Bump `FixtureVersion` whenever the shape
  changes in a way a spec could notice. The SPA pins the version it was written against in
  `e2e/fixtures/seed.ts` (`EXPECTED_FIXTURE_VERSION`) and fails global setup on a mismatch, so a bump
  lands as one legible failure rather than twenty — but only if you bump it.
- The data lives in `E2eFixtureSeeder.cs`. It is **not** an `IPerUserDevSeeder` and has no `Order` —
  do not add it to `SeederOrderBands.md`. See
  [claude/endpoints.md](claude/endpoints.md#the-e2e-fixture-endpoint) for why.
- Watch the constraints that will reject a careless addition: `todo_list_item` is unique on
  `(user_id, activity_id, todo_list_id)`, `activity_history` on `(user_id, activity_id, start_timestamp)`,
  `total_count` is constrained to 2–99 with `done_count <= total_count`, and every `DateTime` must be
  `DateTimeKind.Utc`.
- Add a row assertion to `TestSeedEndpointTests` for anything a spec will depend on. A 200 proves
  nothing about the data, which is the whole reason these gaps existed.
- ⚠ **And a projection assertion, if the point of the row is that a spec can see it.** Those are not
  the same claim. `SuggestedTime` sat on the entity and on both request DTOs but on **no** to-do
  response, so `GET /api/todo-list-item` returned no estimate field at all — the calibration chip and
  the header ratio had nothing to render while every row assertion in the class passed, and the
  fixture looked like the thing at fault. `Seed_LeavesTheEstimateVisibleThroughTheProjection` is the
  shape to copy: run the endpoint's own `IProjectionResponse` expression over the seeded rows and
  assert the field survives it.
