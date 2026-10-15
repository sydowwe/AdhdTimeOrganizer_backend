using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.kind;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Every kind's delivery policy, in one table, read off the real composition root. R1 moves the policy from five
/// scattered places (the catalog options' sets, the static quiet-hours exemption list, the internal e-mail defaults)
/// onto each kind's own definition; this table was written against the old sets <b>before</b> anything moved, so a
/// kind that changes behaviour on the way is a red row with its name on it.
/// <para>
/// Each column is a silent failure on its own: a kind that loses its exemption is an alarm delivered hours late, one
/// that loses its hold buzzes an away user's phone, an e-mail default that flips mails every routine nudge.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationKindPolicyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    public sealed record Policy(
        bool QuietHoursExempt = false,
        bool HeldWhileAway = false,
        bool PushOnByDefault = true,
        bool EmailOnByDefault = false,
        bool AdminOnly = false,
        params NotificationAction[] Actions);

    /// <summary>Every kind this solution registers — the host's catalog plus the two Reminders kinds it does not raise.</summary>
    public static readonly IReadOnlyDictionary<string, Policy> Expected = new Dictionary<string, Policy>
    {
        ["DeadlineApproaching"] = new(EmailOnByDefault: true),
        ["ReminderDigest"] = new(),
        ["Test"] = new(AdminOnly: true),
        ["ScheduledJobFailed"] = new(EmailOnByDefault: true),
        ["ScheduledJobOverdue"] = new(EmailOnByDefault: true),

        ["PersonalReminder"] = new(Actions: [NotificationAction.Snooze(10), NotificationAction.Dismiss]),
        ["PlannedBlockStarting"] = new(HeldWhileAway: true, Actions: [NotificationAction.Start, NotificationAction.Skip]),
        ["PlannedBlocksMissed"] = new(HeldWhileAway: true),
        ["PlannedBlockOverrun"] = new(HeldWhileAway: true, Actions: [NotificationAction.Done, NotificationAction.Extend(15)]),

        ["RoutinePeriodEndingSoon"] = new(HeldWhileAway: true),
        ["RoutinePeriodEnded"] = new(HeldWhileAway: true),
        ["RoutineStreakGraceExpiring"] = new(HeldWhileAway: true),
        ["RoutineSuggestedToday"] = new(HeldWhileAway: true),

        ["TodoItemDue"] = new(Actions: [NotificationAction.Done, NotificationAction.Snooze(60)]),

        ["TimerBoundary"] = new(QuietHoursExempt: true),
        ["StopwatchCheckIn"] = new(),

        ["TrackerSilent"] = new(),

        ["MorningBriefing"] = new(HeldWhileAway: true)
    };

    public static TheoryData<string> Kinds => new(Expected.Keys);

    [Theory(DisplayName = "Each kind's delivery policy is what it was before R1")]
    [MemberData(nameof(Kinds))]
    public void Kind_HasItsPolicy(string key)
    {
        var type = new NotificationType(key);
        var expected = Expected[key];

        Actual(type).Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(),
            $"{key}'s delivery policy must survive the move to its own definition unchanged");
    }

    /// <summary>The table covers every kind there is — a new one gets a row, it does not slip past.</summary>
    [Fact(DisplayName = "The policy table covers exactly the kinds this host catalogues, plus the Reminders module's two")]
    public void Table_CoversEveryKind()
    {
        Expected.Keys.Should().BeEquivalentTo(
            NotificationCatalogTests.Catalog.Select(t => t.Key).Append("DeadlineApproaching").Append("ReminderDigest"));
    }

    /// <summary>
    /// Read through the registry — the one place the dispatcher, the matrix and the action endpoint ask. Admin-only is
    /// read as the two callers see the catalog, so it is a behaviour, not a property of a class.
    /// </summary>
    private Policy Actual(NotificationType type)
    {
        var registry = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationKindRegistry>();
        return new Policy(
            QuietHoursExempt: registry.IsQuietHoursExempt(type),
            HeldWhileAway: registry.IsHeldWhileAway(type),
            PushOnByDefault: registry.IsEnabledByDefault(type, NotificationChannel.WebPush),
            EmailOnByDefault: registry.IsEnabledByDefault(type, NotificationChannel.Email),
            AdminOnly: registry.CatalogFor(isAdmin: true).Contains(type) && !registry.CatalogFor(isAdmin: false).Contains(type),
            Actions: [.. registry.ActionsFor(type)]);
    }
}
