using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <see cref="DailyWindowMaskRequest.From"/> / <see cref="DailyWindowMaskRequest.To"/> are compared
/// straight against <c>WindowStart</c>, a <c>timestamptz</c> column, so a bound value that is not
/// <see cref="DateTimeKind.Utc"/> is an unhandled 500 rather than a wrong answer. Both details
/// endpoints (`desktop/process-details`, `domain-details`) bind this base.
///
/// <para><b>Deliberately not an endpoint test.</b> The one that exists —
/// <c>TrackingDashboardDateRangeTests.ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith</c>
/// — passes either way: <c>Sydowwe.Framework.Testing.PostgresContainerFixture</c> enables
/// <c>Npgsql.EnableLegacyTimestampBehavior</c> for every integration test in the solution and the host
/// does not, so the driver accepts in tests exactly the parameters it rejects in production. Until that
/// diverges no longer (`INFRA-1` in <c>review/history/01-testing.md</c>), a kind has to be asserted on
/// the value itself. No Postgres collection for the same reason — there is nothing here a database
/// would add.</para>
/// </summary>
public class DailyWindowMaskRequestKindTests
{
    /// <summary>
    /// The three shapes a query string can produce. FastEndpoints parses <c>…Z</c> to <c>Utc</c>, an
    /// offset-bearing value to <c>Local</c>, and a bare one to <c>Unspecified</c> — so before the fix
    /// two of the three 500'd, and which one a client got depended on how it happened to format a date.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void BothBounds_AreNormalizedToUtc_WhateverKindTheyWereBoundWith(DateTimeKind kind)
    {
        var bound = DateTime.SpecifyKind(new DateTime(2026, 3, 9, 10, 0, 0), kind);

        var req = new DailyWindowMaskRequest { From = bound, To = bound.AddDays(1) };

        req.From.Kind.Should().Be(DateTimeKind.Utc,
            "Npgsql refuses a non-UTC DateTime at a timestamptz parameter, and this one is compared " +
            "against WindowStart directly");
        req.To.Kind.Should().Be(DateTimeKind.Utc);
    }

    /// <summary>
    /// A <c>Local</c> value is a real instant wearing an offset, so it is converted rather than
    /// relabelled — relabelling it would silently move the envelope by the offset and answer for a
    /// window the user did not select. Skipped when the test host runs in UTC, where the two are the
    /// same operation and the assertion proves nothing.
    /// </summary>
    [Fact]
    public void ALocalBound_IsConverted_NotRelabelled()
    {
        var local = DateTime.SpecifyKind(new DateTime(2026, 3, 9, 10, 0, 0), DateTimeKind.Local);
        if (local.ToUniversalTime().Hour == local.Hour)
            return;

        var req = new DailyWindowMaskRequest { From = local, To = local.AddHours(1) };

        req.From.Should().Be(local.ToUniversalTime());
        req.From.Hour.Should().NotBe(local.Hour, "a relabelled local value would keep the wall-clock hour");
    }

    /// <summary>
    /// An <c>Unspecified</c> value is read as UTC, not shifted by the server's zone. The distinction is
    /// invisible on a UTC build agent and is the whole bug on a developer machine, which is exactly the
    /// asymmetry <c>WallClockZone</c> exists to prevent elsewhere.
    /// </summary>
    [Fact]
    public void AnUnspecifiedBound_IsReadAsUtc_NotAsTheServersLocalTime()
    {
        var bare = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Unspecified);

        var req = new DailyWindowMaskRequest { From = bare, To = bare.AddHours(1) };

        req.From.Should().Be(DateTime.SpecifyKind(bare, DateTimeKind.Utc));
        req.From.Hour.Should().Be(10, "the clock reading is kept; only the kind is supplied");
    }

    /// <summary>
    /// The normalization has to survive `with`, because that is how a record request is copied and a
    /// silently un-normalized clone would reintroduce the 500 somewhere far from here.
    /// </summary>
    [Fact]
    public void TheNormalizationSurvivesAWithExpression()
    {
        var req = new DailyWindowMaskRequest
        {
            From = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Unspecified),
            To = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Unspecified)
        };

        var copy = req with { To = new DateTime(2026, 3, 11, 10, 0, 0, DateTimeKind.Unspecified) };

        copy.From.Kind.Should().Be(DateTimeKind.Utc);
        copy.To.Kind.Should().Be(DateTimeKind.Utc);
    }
}
