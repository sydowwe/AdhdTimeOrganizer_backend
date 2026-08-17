using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The leisure weather signal's judgement, tested without a database or a provider — the reason
/// <see cref="WeatherFitRule"/> is pure, exactly as <see cref="LeisureDrawRanker"/> is.
///
/// <para>What these pin is the asymmetry the feature rests on: "any weather" always fits, snow and dry are not
/// opposites of the same number, and a doubtful day fits rather than not — the signal only ever ranks up, so a
/// false negative costs the user the feature while a false positive costs them one odd card.</para>
/// </summary>
public class WeatherFitRuleTests
{
    private static DailyWeather Day(double precipitation = 0, double snowfall = 0, double maxTemperature = 20,
        double sunshine = 8) =>
        new(precipitation, snowfall, maxTemperature, sunshine);

    [Fact]
    public void AnyWeatherAlwaysFits_IncludingAMiserableDay()
    {
        WeatherFitRule.MatchingCodes(Day(precipitation: 40, snowfall: 0, maxTemperature: 2, sunshine: 0))
            .Should().BeEquivalentTo([WeatherDependencyCodes.None],
                "a row meaning \"indoors, don't care\" is the one thing every day fits");
    }

    [Fact]
    public void AFineDay_FitsSunnyAndDryAndAny_ButNotSnow()
    {
        WeatherFitRule.MatchingCodes(Day(maxTemperature: 24, sunshine: 9))
            .Should().BeEquivalentTo([
                WeatherDependencyCodes.None, WeatherDependencyCodes.Dry, WeatherDependencyCodes.Sunny
            ]);
    }

    [Fact]
    public void ABrightFreezingDay_IsDryButNotSunny()
    {
        // "Wants sun" means a person can enjoy being out in it, not that the sun is technically up.
        var codes = WeatherFitRule.MatchingCodes(Day(maxTemperature: 1, sunshine: 9));

        codes.Should().Contain(WeatherDependencyCodes.Dry);
        codes.Should().NotContain(WeatherDependencyCodes.Sunny);
    }

    [Fact]
    public void AnOvercastDryDay_IsDryButNotSunny()
    {
        var codes = WeatherFitRule.MatchingCodes(Day(sunshine: 1));

        codes.Should().Contain(WeatherDependencyCodes.Dry);
        codes.Should().NotContain(WeatherDependencyCodes.Sunny);
    }

    [Fact]
    public void Drizzle_StillCountsAsDry()
    {
        // Forecast totals this small are inside the provider's own noise, and nobody cancels a walk over them.
        WeatherFitRule.MatchingCodes(Day(precipitation: WeatherFitRule.DryPrecipitationMm))
            .Should().Contain(WeatherDependencyCodes.Dry);

        WeatherFitRule.MatchingCodes(Day(precipitation: WeatherFitRule.DryPrecipitationMm + 0.5))
            .Should().NotContain(WeatherDependencyCodes.Dry);
    }

    [Fact]
    public void ASnowyDay_FitsSnowAndIsNotDry()
    {
        // Snowfall arrives inside the precipitation total, so the two readings must not both be true — but the
        // day is precisely what a "wants snow" activity was waiting for.
        var codes = WeatherFitRule.MatchingCodes(Day(precipitation: 8, snowfall: 6, maxTemperature: -2, sunshine: 0));

        codes.Should().BeEquivalentTo([WeatherDependencyCodes.None, WeatherDependencyCodes.Snow]);
    }

    [Theory]
    [InlineData("Sunny", WeatherDependencyCodes.Sunny)]
    [InlineData("only if it's sunny out", WeatherDependencyCodes.Sunny)]
    [InlineData("Slnečno", WeatherDependencyCodes.Sunny)]
    [InlineData("Snow", WeatherDependencyCodes.Snow)]
    [InlineData("Sneh", WeatherDependencyCodes.Snow)]
    [InlineData("Dry", WeatherDependencyCodes.Dry)]
    [InlineData("None", WeatherDependencyCodes.None)]
    [InlineData("Žiadna", WeatherDependencyCodes.None)]
    [InlineData("Indoors", WeatherDependencyCodes.None)]
    public void ARowWithNoCode_HasOneGuessedFromItsLabel(string text, string expected) =>
        WeatherDependencyCodes.Infer(text).Should().Be(expected,
            "a row the user typed themselves carries no code, and dropping it silently would be worse than a guess");

    [Theory]
    [InlineData("Rainy")]
    [InlineData("Cold")]
    [InlineData("")]
    [InlineData(null)]
    public void ALabelThatMeansNothingKnown_GuessesNothing(string? text) =>
        WeatherDependencyCodes.Infer(text).Should().BeNull(
            "an unrecognised row takes no part in the day's set, which reads as \"no opinion\" rather than as a wrong badge");
}
