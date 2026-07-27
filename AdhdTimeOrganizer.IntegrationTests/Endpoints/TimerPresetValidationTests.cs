using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class TimerPresetValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateTimerPreset_InvalidDuration_Returns400(int duration)
    {
        var request = new { Duration = duration };
        var response = await CreateClient().PostAsJsonAsync("api/timer-preset", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreatePomodoroTimerPreset_InvalidFocusDuration_Returns400(int duration)
    {
        var request = new
        {
            Name = "Test",
            FocusDuration = duration,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1
        };
        var response = await CreateClient().PostAsJsonAsync("api/pomodoro-timer-preset", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePomodoroTimerPreset_EmptyName_Returns400()
    {
        var request = new
        {
            Name = "",
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1
        };
        var response = await CreateClient().PostAsJsonAsync("api/pomodoro-timer-preset", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}