# AdhdTimeOrganizer.ActivityProfiles

> The optional facets of an activity — what kind of thing it is, what it needs, and whether it was
> ever memorable — plus the picker that answers "I'm bored, what should I do?".

## What it does

An `Activity` in Core is a bare name with a role and a category. That is enough to *track* time
against, but not enough to *choose* what to do. This project adds the three profiles that describe an
activity's character:

- **Backlog** — the things you'd like to get around to: energy and effort they take, how many people
  they need, where they happen, what they cost, how long they run, whether they repeat.
- **Project** — DIY work: difficulty, project area, estimated hours, whether it's messy, the materials
  and tools it needs, and how ready it is to start.
- **Bucket list** — the once-in-a-life ones: experience type, how far outside the comfort zone it is,
  whether it involves travel, whether it's a financial goal.

Four per-user lookups (location type, weather dependency, expected cost tier, experience type) give
those profiles their vocabulary, and every user can rename or extend them. A **memory anchor** is the
other end of the same story: "this activity was worth remembering in month X", with a rating and a
note. Anchors are also what makes a bucket-list entry or a one-time backlog entry count as *done* —
nothing stores a "completed" flag.

On top of that sits the **leisure picker**: the user says how long they have, how much energy, who's
around and what they can spend, and gets back a small ranked set of cards drawn from all three
profile pools. The draw is reproducible from a seed in the URL, remembers what it has already offered,
and gets a nudge from today's weather where the user has a location set.

## Setup / running

Nothing module-specific is required to boot — but a few things are worth knowing before you run
anything:

- **Tables live in the `activity_profiles` schema** (`ModuleSchemas.cs` in the host maps this
  assembly to it, anchored on `MemoryAnchor`). You never pass a schema to `ToTable`; a finalizing
  convention applies it. Hand-written SQL is *not* qualified for you.
- **Migrations live in the host** (`AdhdTimeOrganizer/Migrations/`), not here. This is a plain class
  library with no `AppDbContext` reference.
- **The weather signal is optional and off-switchable.** The `LeisureWeather` section in
  `appsettings.json` (`Enabled`, `GeocodingUrl`, `ForecastUrl`, `ForecastCacheMinutes`,
  `GeocodeCacheHours`) configures `OpenMeteoWeatherProvider` — the one outbound HTTP call any slice in
  this solution makes. Open-Meteo needs no API key. Set `Enabled: false` to stop the calls without
  unregistering anything; the endpoint then answers 200 with an empty set, exactly as it does for
  every other failure. A user with no `WeatherLocation` gets the same empty answer.
- **Registering with the host is four places, none of which break the build** — FastEndpoints
  `o.Assemblies`, `ModuleServiceExtensions.ModuleAssemblies`, `AppDbContext.ApplyHostConfigurations`
  and the solution file. See `summary.md` for what each omission silently costs you.
- **Seeders interleave with Core's, on purpose** — the lookups at 10–13 (before Core's `Activity` at
  40) and the profiles/anchors at 50–60 (after it). Read
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before touching any
  `Order` here. The eight `dev/` seeders truncate; the four per-user default seeders do not.

## Docs

- `summary.md` — **start here** if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps
