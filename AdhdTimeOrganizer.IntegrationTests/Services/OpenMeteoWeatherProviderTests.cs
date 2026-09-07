using System.Globalization;
using System.Net;
using System.Text;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.ActivityProfiles.infrastructure.extService.weather;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The one outbound HTTP call the solution makes, driven against a stubbed handler — no database, no network.
///
/// <para>What these pin is the interface's promise that <b>every</b> failure shape is <c>null</c>, which the
/// picker reads as "no weather opinion". The interesting half is the shapes that are not obviously failures: a
/// forecast whose <c>daily</c> block is present but empty used to come back as
/// <c>DailyWeather(0, 0, 0, 0)</c>, and <see cref="WeatherFitRule"/> says yes to <c>dry</c> at zero
/// precipitation — so an answer containing no data badged every dry-weather activity the user owns.</para>
///
/// <para>Also the first coverage of the <c>LeisureWeather:Enabled</c> switch, which until now nothing exercised.</para>
/// </summary>
public class OpenMeteoWeatherProviderTests
{
    private const string Location = "Bratislava, SK";

    private const string GeocodeHit =
        """{"results":[{"latitude":48.1482,"longitude":17.1067}]}""";

    private const string GeocodeMiss = """{"results":[]}""";

    private static string Forecast(string daily, int utcOffsetSeconds = 7200) =>
        $$"""{"utc_offset_seconds":{{utcOffsetSeconds}},"daily":{{daily}}}""";

    private const string CompleteDaily =
        """{"temperature_2m_max":[24.5],"precipitation_sum":[0.2],"snowfall_sum":[0],"sunshine_duration":[32400]}""";

    // ─── the failure shapes that do not look like failures ───────────────────────────────────────────

    [Theory(DisplayName = "A response with no daily block at all is no opinion")]
    [InlineData("""{"utc_offset_seconds":7200}""")]
    [InlineData("""{"utc_offset_seconds":7200,"daily":null}""")]
    public async Task AForecastWithoutADailyBlock_IsNoOpinion(string forecastJson)
    {
        // The shape that was always handled correctly, pinned beside the ones that were not: an omitted key and an
        // explicit null both deserialize to the same missing block, and neither may become a day of zeroes.
        (await CreateProvider(new StubHandler(GeocodeHit, forecastJson))
            .GetTodayAsync(Location, TestContext.Current.CancellationToken))
            .Should().BeNull();
    }

    [Fact(DisplayName = "A daily block whose arrays are all empty is no opinion, not a dry cold day")]
    public async Task AnEmptyDailyBlock_IsNoOpinion()
    {
        var handler = new StubHandler(GeocodeHit, Forecast(
            """{"temperature_2m_max":[],"precipitation_sum":[],"snowfall_sum":[],"sunshine_duration":[]}"""));

        (await CreateProvider(handler).GetTodayAsync(Location, TestContext.Current.CancellationToken))
            .Should().BeNull("zeroes here match \"dry\", which is the code the rule says yes to by default");
    }

    [Fact(DisplayName = "One missing array makes the whole forecast a failed lookup")]
    public async Task APartialDailyBlock_IsNoOpinion()
    {
        var handler = new StubHandler(GeocodeHit, Forecast(
            """{"temperature_2m_max":[24.5],"precipitation_sum":[0.2],"snowfall_sum":[0]}"""));

        (await CreateProvider(handler).GetTodayAsync(Location, TestContext.Current.CancellationToken))
            .Should().BeNull("a response missing sunshine is not a sunless day");
    }

    [Fact(DisplayName = "Nothing partial is cached, so the next call asks again")]
    public async Task APartialForecast_IsNotCached()
    {
        var handler = new StubHandler(GeocodeHit, Forecast("""{"precipitation_sum":[]}"""));
        var provider = CreateProvider(handler);

        await provider.GetTodayAsync(Location, TestContext.Current.CancellationToken);
        await provider.GetTodayAsync(Location, TestContext.Current.CancellationToken);

        handler.ForecastCalls.Should().Be(2, "a bad answer must not be held for three hours");
    }

    // ─── the happy path ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACompleteForecast_LandsInTheRightFields()
    {
        var weather = await CreateProvider(new StubHandler(GeocodeHit, Forecast(CompleteDaily)))
            .GetTodayAsync(Location, TestContext.Current.CancellationToken);

        // The JSON lists temperature first and the record takes it third, which is exactly the kind of thing
        // that transposes silently.
        weather.Should().Be(new DailyWeather(
            PrecipitationMm: 0.2, SnowfallCm: 0, MaxTemperatureC: 24.5, SunshineHours: 9));
    }

    [Fact]
    public async Task TheCoordinatesGoOutInInvariantForm()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sk-SK");
        try
        {
            var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily));
            await CreateProvider(handler).GetTodayAsync(Location, TestContext.Current.CancellationToken);

            handler.LastForecastUrl.Should().Contain("latitude=48.1482",
                "a comma decimal separator would ask the provider for a different place, or for nowhere");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ─── every other way there is no signal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AGeocodingMiss_IsNoOpinion_AndIsCached()
    {
        var handler = new StubHandler(GeocodeMiss, Forecast(CompleteDaily));
        var provider = CreateProvider(handler);

        (await provider.GetTodayAsync("Nowhere at all", TestContext.Current.CancellationToken)).Should().BeNull();
        (await provider.GetTodayAsync("Nowhere at all", TestContext.Current.CancellationToken)).Should().BeNull();

        handler.GeocodeCalls.Should().Be(1,
            "a user who typed nonsense would otherwise re-ask the provider on every draw");
        handler.ForecastCalls.Should().Be(0, "there are no coordinates to ask about");
    }

    [Fact(DisplayName = "LeisureWeather:Enabled = false costs no outbound call at all")]
    public async Task Disabled_IsNoOpinion_AndNoHttp()
    {
        var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily));

        (await CreateProvider(handler, new LeisureWeatherOptions { Enabled = false })
            .GetTodayAsync(Location, TestContext.Current.CancellationToken)).Should().BeNull();

        handler.TotalCalls.Should().Be(0, "the switch exists to stop the traffic, not just the answer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoLocation_IsNoOpinion_AndNoHttp(string location)
    {
        var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily));

        (await CreateProvider(handler).GetTodayAsync(location, TestContext.Current.CancellationToken))
            .Should().BeNull();

        handler.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task AHandlerThatThrows_IsNoOpinion_NotAnException()
    {
        var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily))
        {
            Throw = () => new HttpRequestException("the provider fell over")
        };

        (await CreateProvider(handler).GetTodayAsync(Location, TestContext.Current.CancellationToken))
            .Should().BeNull("the contract is that a third party's bad day never becomes a 500 on the picker");
    }

    [Fact]
    public async Task A500_IsNoOpinion()
    {
        var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily)) { Status = HttpStatusCode.InternalServerError };

        (await CreateProvider(handler).GetTodayAsync(Location, TestContext.Current.CancellationToken))
            .Should().BeNull();
    }

    [Fact]
    public async Task MalformedJson_IsNoOpinion()
    {
        (await CreateProvider(new StubHandler("{ not json", "{ not json"))
            .GetTodayAsync(Location, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ACallerWhoWentAway_Cancels_RatherThanReturningNull()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateProvider(new StubHandler(GeocodeHit, Forecast(CompleteDaily)))
            .GetTodayAsync(Location, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "an abandoned request is not a weather outage, and swallowing it would hide it");
    }

    // ─── the cache window ────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A forecast is never cached past the next midnight where the user is")]
    public async Task TheForecastCache_IsCappedAtLocalMidnight()
    {
        // Pick the offset that puts local time at 23:50, whatever time it is when this runs.
        var minutesToLocalMidnight = 10;
        var localTarget = new TimeSpan(23, 60 - minutesToLocalMidnight, 0);
        var offset = (int)(localTarget - DateTime.UtcNow.TimeOfDay).TotalSeconds;

        var cache = new RecordingCache();
        var handler = new StubHandler(GeocodeHit, Forecast(CompleteDaily, offset));

        await CreateProvider(handler, cache: cache).GetTodayAsync(Location, TestContext.Current.CancellationToken);

        cache.LastForecastExpiry.Should().NotBeNull()
            .And.BeCloseTo(TimeSpan.FromMinutes(minutesToLocalMidnight), TimeSpan.FromMinutes(1),
                "a plain three-hour TTL would serve yesterday's forecast into the small hours");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static OpenMeteoWeatherProvider CreateProvider(
        StubHandler handler, LeisureWeatherOptions? options = null, IMemoryCache? cache = null) =>
        new(new HttpClient(handler),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new LeisureWeatherOptions()),
            NullLogger<OpenMeteoWeatherProvider>.Instance);

    /// <summary>The provider makes exactly two kinds of call; the geocoding one is the one whose URL says "search".</summary>
    private sealed class StubHandler(string geocodeJson, string forecastJson) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Func<Exception>? Throw { get; init; }
        public int GeocodeCalls { get; private set; }
        public int ForecastCalls { get; private set; }
        public int TotalCalls => GeocodeCalls + ForecastCalls;
        public string? LastForecastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Throw is not null)
                throw Throw();

            var url = request.RequestUri!.ToString();
            var isGeocode = url.Contains("search", StringComparison.OrdinalIgnoreCase);
            if (isGeocode)
            {
                GeocodeCalls++;
            }
            else
            {
                ForecastCalls++;
                LastForecastUrl = url;
            }

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(isGeocode ? geocodeJson : forecastJson, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// A real <see cref="MemoryCache"/> with the relative expiry of each written entry recorded — the only way to
    /// assert on the cap without waiting out a clock.
    /// </summary>
    private sealed class RecordingCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());

        public TimeSpan? LastForecastExpiry { get; private set; }

        public ICacheEntry CreateEntry(object key) => new RecordingEntry(_inner.CreateEntry(key), this);

        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

        public void Remove(object key) => _inner.Remove(key);

        public void Dispose() => _inner.Dispose();

        private sealed class RecordingEntry(ICacheEntry inner, RecordingCache owner) : ICacheEntry
        {
            public object Key => inner.Key;

            public object? Value
            {
                get => inner.Value;
                set => inner.Value = value;
            }

            public DateTimeOffset? AbsoluteExpiration
            {
                get => inner.AbsoluteExpiration;
                set => inner.AbsoluteExpiration = value;
            }

            public TimeSpan? AbsoluteExpirationRelativeToNow
            {
                get => inner.AbsoluteExpirationRelativeToNow;
                set
                {
                    inner.AbsoluteExpirationRelativeToNow = value;
                    if (inner.Key is string key && key.Contains("forecast"))
                        owner.LastForecastExpiry = value;
                }
            }

            public TimeSpan? SlidingExpiration
            {
                get => inner.SlidingExpiration;
                set => inner.SlidingExpiration = value;
            }

            public IList<IChangeToken> ExpirationTokens => inner.ExpirationTokens;

            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;

            public CacheItemPriority Priority
            {
                get => inner.Priority;
                set => inner.Priority = value;
            }

            public long? Size
            {
                get => inner.Size;
                set => inner.Size = value;
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
