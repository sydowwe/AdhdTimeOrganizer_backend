namespace AdhdTimeOrganizer.application.dto.testSupport;

/// <summary>
/// What <c>POST /api/test/seed</c> returns once the database is in the known state.
///
/// <para>The body exists so a spec can stop hardcoding ids. Everything in it is stable across calls —
/// the id maps are the compile-time constants in <c>E2eFixtureIds</c>, and the lookup maps
/// (<see cref="TaskPriorities"/>, <see cref="RoutineTimePeriods"/>) are the per-user defaults the app
/// creates on sign-up, read back after seeding because the seeder does not choose their ids.</para>
/// </summary>
/// <param name="Fixture">Fixture version, bumped when the shape changes in a way a spec could notice.</param>
/// <param name="SeedDateUtc">
/// The UTC date every clock-relative value was computed from. Two calls on the same UTC day produce
/// identical dates; a call either side of midnight UTC does not, which is the point of the field.
/// </param>
public sealed record TestSeedResponse(
    string Fixture,
    DateOnly SeedDateUtc,
    TestSeedAccount PrimaryUser,
    TestSeedAccount SecondaryUser,
    TestSeedAccount AdminUser,
    IReadOnlyDictionary<string, long> Activities,
    IReadOnlyDictionary<string, long> TodoLists,
    IReadOnlyDictionary<string, long> TodoListItems,
    IReadOnlyDictionary<string, long> Profiles,
    IReadOnlyDictionary<string, long> Routines,
    IReadOnlyDictionary<string, long> DayPlannerTemplates,
    IReadOnlyDictionary<string, long> TrackingMappings,
    IReadOnlyDictionary<string, long> Notifications,
    IReadOnlyDictionary<string, long> Reminders,
    IReadOnlyDictionary<string, long> ReminderDefinitions,
    IReadOnlyDictionary<string, long> Scheduler,
    IReadOnlyDictionary<string, long> TaskPriorities,
    IReadOnlyDictionary<string, long> RoutineTimePeriods,
    TestSeedCounts Counts);

/// <param name="Reminders">
/// The portal's own reminder rows — what the user's own reminder screens address.
/// </param>
/// <param name="ReminderDefinitions">
/// The module-side definition for each of those, keyed the same way. These are the ids the admin register
/// and its detail route take, and they are deliberately not the same numbers as <paramref name="Reminders"/>
/// so a spec cannot pass one where it means the other and still get a 200.
/// </param>
/// <param name="Scheduler">
/// <c>orphanedJob</c> and <c>orphanedJobRun</c> — the one registry row whose handler no longer exists, and a
/// run of it. Resolved rather than pinned: the scheduler schema survives the reset, so these ids are whatever
/// its sequence gave the rows the first time, and they stay put across resets because the rows are rewritten
/// in place.
/// </param>
/// <param name="Password">
/// Echoed back on purpose: this endpoint only exists where the account it describes is a fixture, and
/// a spec that reads the credential from the seed response cannot drift from the seeder the way a
/// second copy in <c>.env.test</c> can.
/// </param>
public sealed record TestSeedAccount(long Id, string Email, string Password, string Locale);

/// <param name="OverdueTodoListItems">
/// How many not-done items are past their due date. Reported rather than assumed: the
/// overdue-renegotiation banner's threshold lives in the SPA, so the fixture aims comfortably above any
/// plausible value and this is how a spec confirms it did.
/// </param>
/// <param name="CalibrationTodoListItems">
/// Items carrying a non-zero <c>suggestedTime</c> whose activity also has history rows — the input to
/// the estimate-vs-actual chip and the header ratio line.
/// </param>
/// <param name="DistinctDesktopEntries">
/// How many <c>(productName, processName, windowTitle)</c> groups the desktop ledger holds — the row count
/// <c>/activity-tracking/desktop/settings/distinctEntries</c> shows, which is not the entry count and is the
/// number a spec paginating that grid actually needs.
/// </param>
/// <param name="UnreadNotifications">
/// Strictly smaller than <paramref name="Notifications"/>, which is what makes the bell's <c>All</c> /
/// <c>Unread</c> filter distinguishable at all.
/// </param>
/// <param name="UpcomingReminders">
/// Definitions with a pending occurrence — the row count the admin upcoming grid pages, and strictly
/// smaller than <paramref name="ReminderDefinitions"/> because the fixture also holds a paused one and a
/// cancelled one.
/// </param>
/// <param name="ReminderFailuresNeedingAttention">
/// Failed dispatches no reversal row supersedes. Smaller than the failed-row count on purpose: the fixture
/// carries one failure that stands and one that was reversed, so a count that ignores the reversal lineage
/// reports the wrong number here and nowhere else.
/// </param>
/// <param name="RoutinePeriodCompletions">
/// Closed routine cycles in the completion ledger, across the primary account's periods — what the heatmaps,
/// the trend arrow and the consistency figure are computed over. Zero until <c>e2e-v4</c>, which is why none
/// of those had ever rendered.
/// </param>
/// <param name="FrozenRoutinePeriodCompletions">
/// Of those, the ones a streak freeze was spent on — the heatmap's third cell state, which the SPA never
/// invents client-side and which is therefore reachable only from a real row.
/// </param>
public sealed record TestSeedCounts(
    int Activities,
    int TodoListItems,
    int OverdueTodoListItems,
    int CalibrationTodoListItems,
    int ActivityHistoryEntries,
    int PlannerTasks,
    int Calendars,
    int DayPlannerTemplates,
    int TemplatePlannerTasks,
    int WebExtensionEntries,
    int DesktopEntries,
    int DistinctDesktopEntries,
    int AndroidSessions,
    int DistinctAndroidEntries,
    int Notifications,
    int UnreadNotifications,
    int ReminderDefinitions,
    int UpcomingReminders,
    int ReminderDispatches,
    int ReminderFailuresNeedingAttention,
    int RoutinePeriodCompletions,
    int FrozenRoutinePeriodCompletions);
