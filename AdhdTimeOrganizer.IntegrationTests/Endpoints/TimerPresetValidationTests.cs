using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class TimerPresetValidationTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateTimerPreset_InvalidDuration_Returns400(int duration)
    {
        var request = new { Duration = duration };
        var response = await client.PostAsJsonAsync("timer-preset", request);
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
        var response = await client.PostAsJsonAsync("pomodoro-timer-preset", request);
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
        var response = await client.PostAsJsonAsync("pomodoro-timer-preset", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
