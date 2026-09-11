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
    IReadOnlyDictionary<string, long> Activities,
    IReadOnlyDictionary<string, long> TodoLists,
    IReadOnlyDictionary<string, long> TodoListItems,
    IReadOnlyDictionary<string, long> Profiles,
    IReadOnlyDictionary<string, long> Routines,
    IReadOnlyDictionary<string, long> DayPlannerTemplates,
    IReadOnlyDictionary<string, long> TrackingMappings,
    IReadOnlyDictionary<string, long> Notifications,
    IReadOnlyDictionary<string, long> TaskPriorities,
    IReadOnlyDictionary<string, long> RoutineTimePeriods,
    TestSeedCounts Counts);

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
    int UnreadNotifications);
