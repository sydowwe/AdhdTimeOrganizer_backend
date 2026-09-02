using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Every persisted <see cref="DateTimeOffset"/> reaches the database at offset zero.
///
/// <para>Npgsql maps <see cref="DateTimeOffset"/> to <c>timestamptz</c> and refuses any other offset
/// outright, so a column without this conversion is an unhandled 500 at <c>SaveChanges</c> — and only
/// for users outside UTC, because a UTC user's offset is zero already. That is how it stayed hidden:
/// <c>NotificationService</c> stamps the quiet-hours deferral on the <i>recipient's</i> clock, so the
/// three suites written to prove per-recipient zone handling were the only ones that could fail, and
/// the fixture user is UTC.</para>
///
/// <para><b>Asserted over the whole model rather than on the two columns that failed.</b> There were 14
/// such columns when this was written and 11 of them are in Reminders, where nothing drives a zoned
/// value — they were latent, not safe. Walking the model means a column added later is covered the day
/// it appears instead of the day someone notices, which is the difference between this guard and a
/// test per property.</para>
///
/// <para><b>Asserted on behaviour, not on the presence of a converter</b>, and deliberately independent
/// of <c>Npgsql.EnableLegacyTimestampBehavior</c>: the harness turns that switch on and the host does
/// not (<c>INFRA-1</c>), so a round trip proves nothing here — it accepts in tests exactly what
/// production rejects. This runs the converter and checks the value it produces.</para>
/// </summary>
[Collection("Postgres")]
public class DateTimeOffsetUtcConventionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>An offset no fixture uses, so a converter that merely passes the value through fails.</summary>
    private static readonly DateTimeOffset Zoned =
        new(2026, 3, 9, 10, 0, 0, TimeSpan.FromHours(12));

    [Fact]
    public void EveryPersistedDateTimeOffset_IsConvertedToOffsetZero()
    {
        using var db = CreateDbContext();

        var offending = new List<string>();

        foreach (var entityType in db.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
        {
            var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (clr != typeof(DateTimeOffset))
                continue;

            var converter = property.GetValueConverter();
            if (converter is null)
            {
                offending.Add($"{entityType.ShortName()}.{property.Name} (no converter)");
                continue;
            }

            var stored = converter.ConvertToProvider(Zoned);
            if (stored is DateTimeOffset { Offset.Ticks: not 0 } wrong)
                offending.Add($"{entityType.ShortName()}.{property.Name} (kept offset {wrong.Offset})");
        }

        offending.Should().BeEmpty(
            "Npgsql writes DateTimeOffset to timestamptz and accepts offset 0 only, so any column " +
            "reaching it with a zone offset throws for exactly the users who are not in UTC. The " +
            "conversion is applied model-wide by BaseDbContext.ConfigureConventions — a host that " +
            "overrides that method without calling base drops it for every column at once");
    }

    /// <summary>
    /// The conversion keeps the instant. Worth pinning separately because "normalize to UTC" is
    /// implementable as <c>SpecifyKind</c>-style relabelling too, which would silently move every
    /// deferral by the recipient's offset — a wrong time that still writes cleanly, and so is far worse
    /// than the 500 this replaced.
    /// </summary>
    [Fact]
    public void TheConversion_PreservesTheInstant()
    {
        using var db = CreateDbContext();

        var converter = db.Model
            .FindEntityType(typeof(Sydowwe.Notifications.domain.entity.Notification))!
            .GetProperty(nameof(Sydowwe.Notifications.domain.entity.Notification.DeferredUntil))
            .GetValueConverter();

        converter.Should().NotBeNull();

        var stored = (DateTimeOffset)converter!.ConvertToProvider(Zoned)!;

        stored.Offset.Should().Be(TimeSpan.Zero);
        stored.UtcDateTime.Should().Be(Zoned.UtcDateTime, "the same instant, written in UTC terms");
        stored.Hour.Should().Be(22, "10:00+12:00 is 22:00 the previous day in UTC");
        stored.Day.Should().Be(8);
    }
}
