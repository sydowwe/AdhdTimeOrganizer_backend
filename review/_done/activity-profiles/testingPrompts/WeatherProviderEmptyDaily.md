# TEST-9 — an empty `daily` block must mean "no weather opinion", not "a dry day"

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **CQ-5 🟠**, and is the first coverage `OpenMeteoWeatherProvider` has ever had (the provider as a
whole is a *previously known* gap in `docs/testing.md`; this prompt covers one behaviour of it, not
the gap).

## The defect

`infrastructure/extService/weather/OpenMeteoWeatherProvider.cs`:

- `:108-110` — a **missing** `daily` object returns `null`. Correct.
- `:112-117` + `:132` — a **present but empty** `daily` block goes through
  `First(double[]? values) => values is { Length: > 0 } ? values[0] : 0`, producing
  `DailyWeather(0, 0, 0, 0)`.
- `domain/service/WeatherFitRule.cs:41-44` — `PrecipitationMm <= 1.0` is true, so `Dry` joins the
  matching set.

Result: `GET /api/leisure-weather-fit` paints a "good for today" badge on every dry-weather activity
from a response that contained no data. The contract, stated in three places
(`IDailyWeatherProvider.cs:6-9`, `GetLeisureWeatherFitEndpoint.cs:23-27`, `docs/summary.md:102-108`),
is that every failure shape is an **empty** set.

## Two tests, in two places

### 1. Provider unit test — new `Services/OpenMeteoWeatherProviderTests.cs`

There is no existing suite for this class, so you are establishing it. It needs no database, only a
stubbed `HttpMessageHandler`, an `IMemoryCache` and `IOptions<LeisureWeatherOptions>`:

```csharp
var provider = new OpenMeteoWeatherProvider(
    new HttpClient(new StubHandler(geocodeJson, forecastJson)),
    new MemoryCache(new MemoryCacheOptions()),
    Options.Create(new LeisureWeatherOptions()),
    NullLogger<OpenMeteoWeatherProvider>.Instance);
```

`StubHandler` returns the geocoding payload for a URL containing `search` and the forecast payload
otherwise — the provider makes exactly two calls (`GeocodeAsync:75`, `ForecastAsync:103-105`).

Scenarios:

1. **`daily` present, every array empty** → `GetTodayAsync` returns **null**. Fails today (returns
   `DailyWeather(0,0,0,0)`).
2. **`daily` present, one array missing** (e.g. no `sunshine_duration`) → **null**. A partial
   response is a failed lookup, not a sunless day.
3. **A complete response maps correctly**, including `sunshine_duration` seconds → hours
   (`:117` divides by 3600) and the four fields landing in the right `DailyWeather` positions — the
   constructor takes `(precipitation, snowfall, maxTemp, sunshineHours)` while the JSON block lists
   temperature first, which is exactly the kind of thing that silently transposes.
4. **Geocoding miss** (`results: []`) → null, **and the miss is cached**: a second call makes no
   further HTTP request (`:79-82`). Count the handler's invocations.
5. **`Enabled = false`** → null with **zero** HTTP calls (`:37`). This also closes the second
   *previously known* gap in `docs/testing.md`, so mention that in the `DisplayName`.
6. **A handler that throws** → null, not an exception (`:55-61`), and a handler that returns 500 →
   null (`:137-141`).
7. **Cache duration is capped at the next local midnight** (`CacheDuration:123-128`): with a
   `utc_offset_seconds` that puts local time at 23:50, the entry must expire in ~10 minutes rather
   than the configured 180. Assert through the `IMemoryCache` entry rather than by waiting.

### 2. Endpoint-level pin — extend `Endpoints/LeisureWeatherFitTests.cs`

Add a fourth double beside `StubWeatherProvider` and `ThrowingWeatherProvider` (`:299`, `:310`) — or
just use `StubWeatherProvider(new DailyWeather(0, 0, 0, 0))` — and assert that
`GET /api/leisure-weather-fit` returns **only** the "none / any weather" row's id, **not** the "Dry"
row's. That is the user-visible half: today a zeroed forecast badges the user's dry-weather
activities.

Careful: `WeatherDependencyCodes.None` is always in the matching set by design
(`WeatherFitRule.cs:37`), so the empty-signal assertion at the *endpoint* is "the Dry id is absent",
not "the list is empty" — the list is only truly empty when the provider returns null. Both
assertions belong in the file, each with the reason in its `DisplayName`.

## The fix

In `ForecastAsync`, return `null` unless all four arrays are non-empty — before constructing
`DailyWeather`, so nothing is cached. Keep `First` for the indexing, or fold it into a
`TryFirst(out double)` helper.
