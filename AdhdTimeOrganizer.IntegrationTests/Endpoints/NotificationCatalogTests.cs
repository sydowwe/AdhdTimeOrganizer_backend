using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Notifications.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// This host's run of the Notifications module's catalog contract (backend ask B4) — the rules live in
/// <see cref="BaseNotificationCatalogTests"/>, in the module's own testing package, so every host that ships
/// the module inherits them instead of copying them.
/// <para>
/// All this portal owes the base is the list below: the ten kinds it can raise, out of the nineteen the
/// shared <see cref="NotificationType"/> enum carries. It is deliberately a <b>second copy</b> of what
/// <c>ModuleServiceExtensions.AddNotificationCatalog</c> declares — reading the registration back would
/// assert that the composition root equals itself, and the failure this guards against is someone editing
/// that list without meaning to change what the settings screen offers.
/// </para>
/// <para>
/// The nine kinds left out (<c>StockLow</c>, the leave/work-log family, <c>RegistratoryDisposalDue</c>,
/// <c>UpcomingHrEvents</c>) belong to the HR/inventory product that shares the contracts assembly. There is
/// no inventory and no leave in an ADHD time organiser, and no locale here names them.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationCatalogTests(AppDbContextFixture fixture) : BaseNotificationCatalogTests(fixture)
{
    protected override IReadOnlySet<NotificationType> ExpectedCatalog => Catalog;

    /// <summary>Shared with <see cref="NotificationVocabularyTests"/>: one second copy, two contracts keyed on it.</summary>
    public static readonly IReadOnlySet<NotificationType> Catalog = new HashSet<NotificationType>
    {
        NotificationType.DeadlineApproaching,
        NotificationType.ReminderDigest,
        NotificationType.PersonalReminder,
        NotificationType.RoutinePeriodEndingSoon,
        NotificationType.RoutinePeriodEnded,
        NotificationType.RoutineStreakGraceExpiring,
        NotificationType.ScheduledJobFailed,
        NotificationType.ScheduledJobOverdue,
        NotificationType.TimerBoundary,
        NotificationType.Test
    };
}
