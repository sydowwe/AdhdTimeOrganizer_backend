using AdhdTimeOrganizer.application.service.morningBriefing;
using AdhdTimeOrganizer.History.application.notification;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.notification;
using AdhdTimeOrganizer.Routines.application.notification;
using AdhdTimeOrganizer.TodoLists.application.notification;
using AdhdTimeOrganizer.Tracking.application.notification;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Notifications.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// This host's run of the Notifications module's catalog contract (backend ask B4) — the rules live in
/// <see cref="BaseNotificationCatalogTests"/>, in the module's own testing package, so every host that ships
/// the module inherits them instead of copying them.
/// <para>
/// All this portal owes the base is the list below: the kinds it can raise. It is deliberately a <b>second
/// copy</b> of what the composition produces — every registered kind less
/// <c>ModuleServiceExtensions.AddNotificationCatalog</c>'s <c>UnraisedTypes</c> — because reading the registry
/// back would assert that the composition root equals itself. The failures this guards against are a slice
/// dropping out of the scanned assemblies (its kinds vanish from the settings screen with no error) and a new kind
/// appearing on that screen without anyone meaning it to.
/// </para>
/// <para>
/// The two registered kinds left out, <c>DeadlineApproaching</c> and <c>ReminderDigest</c>, are the Reminders
/// module's, and nothing here raises them: Planning's personal reminders carry their own kind and no digest key.
/// They come back with a producer.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationCatalogTests(AppDbContextFixture fixture) : BaseNotificationCatalogTests(fixture)
{
    protected override IReadOnlySet<NotificationType> ExpectedCatalog => Catalog;

    /// <summary>Shared with <see cref="NotificationVocabularyTests"/>: one second copy, two contracts keyed on it.</summary>
    public static readonly IReadOnlySet<NotificationType> Catalog = new HashSet<NotificationType>
    {
        PlanningNotificationTypes.PersonalReminder,
        RoutinesNotificationTypes.RoutinePeriodEndingSoon,
        RoutinesNotificationTypes.RoutinePeriodEnded,
        RoutinesNotificationTypes.RoutineStreakGraceExpiring,
        RoutinesNotificationTypes.RoutineSuggestedToday,
        FrameworkNotificationTypes.ScheduledJobFailed,
        FrameworkNotificationTypes.ScheduledJobOverdue,
        TodoListsNotificationTypes.TodoItemDue,
        PlanningNotificationTypes.PlannedBlockStarting,
        PlanningNotificationTypes.PlannedBlocksMissed,
        PlanningNotificationTypes.PlannedBlockOverrun,
        PortalNotificationTypes.MorningBriefing,
        HistoryNotificationTypes.TimerBoundary,
        HistoryNotificationTypes.StopwatchCheckIn,
        TrackingNotificationTypes.TrackerSilent,
        FrameworkNotificationTypes.Test
    };

    protected override IReadOnlyDictionary<NotificationType, IReadOnlyList<NotificationAction>> ExpectedActions => Actions;

    /// <summary>
    /// P2-03's table — the second copy of the buttons each kind declares (<c>INotificationKind.Actions</c>).
    /// Shared with <see cref="NotificationPushActionTests"/>, which drives every button in it end to end.
    /// </summary>
    public static readonly IReadOnlyDictionary<NotificationType, IReadOnlyList<NotificationAction>> Actions =
        new Dictionary<NotificationType, IReadOnlyList<NotificationAction>>
        {
            [PlanningNotificationTypes.PersonalReminder] = [NotificationAction.Snooze(10), NotificationAction.Dismiss],
            [TodoListsNotificationTypes.TodoItemDue] = [NotificationAction.Done, NotificationAction.Snooze(60)],
            [PlanningNotificationTypes.PlannedBlockStarting] = [NotificationAction.Start, NotificationAction.Skip],
            [PlanningNotificationTypes.PlannedBlockOverrun] = [NotificationAction.Done, NotificationAction.Extend(15)]
        };
}
