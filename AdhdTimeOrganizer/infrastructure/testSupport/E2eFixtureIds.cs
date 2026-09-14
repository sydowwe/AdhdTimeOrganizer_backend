namespace AdhdTimeOrganizer.infrastructure.testSupport;

/// <summary>
/// The primary keys <c>POST /api/test/seed</c> guarantees. A Playwright spec may reference any value
/// here directly; they are also echoed in the seed response so a spec can read them instead.
///
/// <para><b>Why explicit ids and not "whatever the sequence produced".</b> The reset truncates with
/// <c>RESTART IDENTITY</c>, so auto-generated ids are already reproducible — but only as long as the
/// row counts ahead of them never change, and one of those counts is "every calendar day in two
/// years", which moves on a leap year. Writing the ids the specs assert on makes them independent of
/// everything inserted before them.</para>
///
/// <para><b>The bands are chosen to sit above every auto-generated id in the fixture.</b> The largest
/// auto-generated set is <c>calendar</c> — roughly 730 rows per user for four users (the root admin and
/// the three fixture accounts), so about 2 900 after a reset. Everything here starts at 10 000.
/// <see cref="E2eFixtureSeeder"/> advances each
/// touched sequence past its band afterwards, so rows created by the app <i>during</i> a test run
/// cannot land on a fixture id either.</para>
///
/// <para>⚠ These values are a contract with the e2e suite in the SPA repo. Changing one silently
/// breaks a spec that hardcoded it; add a new id rather than renumbering an existing one.</para>
/// </summary>
public static class E2eFixtureIds
{
    /// <summary>Bumped whenever the fixture's shape changes in a way a spec could notice.</summary>
    /// <remarks>
    /// <c>e2e-v2</c> added the four surfaces that had no rows at all: day-planner templates, the three
    /// tracking ledgers with their pattern mappings, and the notification bell.
    /// <para>
    /// <c>e2e-v3</c> added the admin account and the reminders register: the five
    /// <c>/reminder-definition</c> and <c>/reminder-dashboard</c> admin routes answered 403 for every
    /// fixture account and had no rows to show even if they had not.
    /// </para>
    /// <para>
    /// <c>e2e-v4</c> added routine <i>period</i> history: the completion ledger behind the heatmaps, the
    /// period-level streaks the personal-bests panel reads, and four more daily routines whose item streaks
    /// reach the four milestone tiers above 7 that had never rendered.
    /// </para>
    /// </remarks>
    public const string FixtureVersion = "e2e-v4";

    // --- accounts -------------------------------------------------------------------------------
    // Credentials mirror the SPA's e2e/.env.test. The password is stored as a hash written directly by
    // the seeder rather than through UserManager: it does not satisfy this app's own registration
    // policy (10 chars, upper, digit, non-alphanumeric), and sign-in only ever checks the hash.
    public const long PrimaryUserId = 10_001;
    public const string PrimaryUserEmail = "test@example.com";
    public const string PrimaryUserPassword = "testpass123";

    public const long SecondaryUserId = 10_002;
    public const string SecondaryUserEmail = "test2@example.com";
    public const string SecondaryUserPassword = "testpass123";

    /// <summary>
    /// The account that carries the <c>Admin</c> role, for the framework modules' admin surface — the
    /// reminders register and its three dashboards, which roll up <i>every</i> user's reminders and are
    /// gated on Admin for that reason.
    ///
    /// <para><b>Why a third account rather than promoting <see cref="PrimaryUserId"/>.</b> The primary
    /// account is what almost every spec signs in as; making it an admin would make every admin-gated
    /// route in the app reachable from those specs, and a gate nothing is ever denied by is a gate no
    /// test can notice losing. Keeping the primary a plain <c>User</c> keeps "an ordinary user gets 403
    /// here" assertable, which is the property that would have caught this fixture gap.</para>
    ///
    /// <para>It carries <c>User</c> as well as <c>Admin</c>: a real administrator is also a user of the
    /// app, and the account needs its own ordinary surface to be reachable for the admin views' own
    /// navigation to work.</para>
    /// </summary>
    public const long AdminUserId = 10_003;
    public const string AdminUserEmail = "admin@example.com";
    public const string AdminUserPassword = "testpass123";

    // --- activity roles -------------------------------------------------------------------------
    // On top of the three system-keyed roles IUserDefaultsService creates for every account.
    public const long PrimaryRoleWork = 11_001;
    public const long PrimaryRoleHealth = 11_002;
    public const long PrimaryRoleLeisure = 11_003;
    public const long SecondaryRoleWork = 11_101;

    // --- activity categories --------------------------------------------------------------------
    public const long PrimaryCategoryDeepWork = 11_501;
    public const long PrimaryCategoryQuickTasks = 11_502;
    public const long PrimaryCategoryErrands = 11_503;
    public const long SecondaryCategoryGeneral = 11_601;

    // --- activities -----------------------------------------------------------------------------
    // 12_001–12_006 are the calibration set: each carries a to-do item with a SuggestedTime AND three
    // ActivityHistory rows, which is what the estimate-vs-actual surfaces need to render.
    public const long ActivityBugFixing = 12_001;
    public const long ActivityFeatureDevelopment = 12_002;
    public const long ActivityCodeReview = 12_003;
    public const long ActivityMorningExercise = 12_004;
    public const long ActivityGroceryShopping = 12_005;
    public const long ActivityMealPreparation = 12_006;

    // The three leisure profiles, one per kind, on distinct activities.
    public const long ActivityReadANovel = 12_007;
    public const long ActivityLearnBouldering = 12_008;
    public const long ActivityBuildABookshelf = 12_009;

    /// <summary>Archived. Keeps its rows, disappears from every picker.</summary>
    public const long ActivityRetiredHobby = 12_010;

    /// <summary>Referenced by an ActivityHistory row and nothing else, so the grid reports canDelete = false.</summary>
    public const long ActivityWriteBlogPost = 12_011;

    /// <summary>Referenced by nothing, so the grid reports canDelete = true — the contrast case.</summary>
    public const long ActivityUnusedIdea = 12_012;

    // 12_013–12_018 carry the overdue items. Separate activities rather than reusing the calibration
    // set: todo_list_item is unique on (user_id, activity_id, todo_list_id).
    public const long ActivityRenewPassport = 12_013;
    public const long ActivityFileTaxReturn = 12_014;
    public const long ActivityCallTheDentist = 12_015;
    public const long ActivityFixTheBike = 12_016;
    public const long ActivityReplyToLandlord = 12_017;
    public const long ActivityReturnLibraryBook = 12_018;

    /// <summary>Carries the partially-complete multi-count item.</summary>
    public const long ActivityDeepCleanKitchen = 12_019;

    // 12_020–12_022 carry the routines.
    public const long ActivityTakeMedication = 12_020;
    public const long ActivityWeeklyReview = 12_021;
    public const long ActivityPayRent = 12_022;

    /// <summary>
    /// The one activity desktop tracking attributes time to. Deliberately its own activity rather than one
    /// of the calibration set: the attribution rows are written by the real seam
    /// (<c>IActivityTimeAttributionSink</c>), and folding machine-recorded time into the six activities the
    /// estimate-vs-actual surfaces average over would move numbers a spec asserts on for an unrelated reason.
    /// </summary>
    public const long ActivityDeskResearch = 12_023;

    // 12_024–12_027 carry the four milestone routines — see RoutineDrinkWater and its neighbours.
    public const long ActivityDrinkWater = 12_024;
    public const long ActivityJournal = 12_025;
    public const long ActivityMakeTheBed = 12_026;
    public const long ActivityBrushTeeth = 12_027;

    public const long SecondaryActivityDraftProposal = 12_101;
    public const long SecondaryActivityPostParcel = 12_102;

    // --- to-do lists ----------------------------------------------------------------------------
    public const long PrimaryListFocus = 13_001;
    public const long PrimaryListErrands = 13_002;

    /// <summary>Deliberately empty, so the list empty-state is reachable.</summary>
    public const long PrimaryListSomeday = 13_003;

    public const long SecondaryListWork = 13_101;

    // --- to-do items ----------------------------------------------------------------------------
    // 14_001–14_006: SuggestedTime set, matching history on the same activity. Six, not five, so the
    // header ratio line stays above its "≥5 comparable items" threshold if one is completed mid-run.
    public const long ItemBugFixing = 14_001;
    public const long ItemFeatureDevelopment = 14_002;
    public const long ItemCodeReview = 14_003;
    public const long ItemMorningExercise = 14_004;
    public const long ItemGroceryShopping = 14_005;
    public const long ItemMealPreparation = 14_006;

    // 14_007–14_012: overdue and not done, 1 to 14 days past due.
    public const long ItemRenewPassport = 14_007;
    public const long ItemFileTaxReturn = 14_008;
    public const long ItemCallTheDentist = 14_009;
    public const long ItemFixTheBike = 14_010;
    public const long ItemReplyToLandlord = 14_011;
    public const long ItemReturnLibraryBook = 14_012;

    /// <summary>DoneCount 2 of TotalCount 5, not done — the multi-count counter mid-progress.</summary>
    public const long ItemDeepCleanKitchen = 14_013;

    /// <summary>Completed today, so the daily recap has a row.</summary>
    public const long ItemCodeReviewDone = 14_014;

    /// <summary>Paired with <see cref="ActivityReadANovel"/> — a temptation bundle.</summary>
    public const long ItemBuildABookshelf = 14_015;

    public const long SecondaryItemDraftProposal = 14_101;
    public const long SecondaryItemPostParcel = 14_102;

    // --- activity history -----------------------------------------------------------------------
    /// <summary>The calibration rows run 15_001–15_018: three per activity for 12_001–12_006.</summary>
    public const long HistoryCalibrationFirst = 15_001;

    /// <summary>The single row that makes <see cref="ActivityWriteBlogPost"/> undeletable.</summary>
    public const long HistoryWriteBlogPost = 15_101;

    public const long SecondaryHistoryDraftProposal = 15_501;

    // --- planner tasks --------------------------------------------------------------------------
    /// <summary>Runs 16_001–16_007 across today, tomorrow and today+3. Today+2 is left empty on purpose.</summary>
    public const long PlannerTaskFirst = 16_001;

    // --- leisure profiles and memory anchors ----------------------------------------------------
    public const long BacklogProfileReadANovel = 17_001;
    public const long BucketListProfileLearnBouldering = 17_101;
    public const long ProjectProfileBuildABookshelf = 17_201;

    /// <summary>On the bucket-list activity, so "close the loop" has something to close.</summary>
    public const long MemoryAnchorLearnBouldering = 18_001;

    /// <summary>
    /// On the archived activity — the case the anchor grid exists to hold, since an anchor records
    /// something already done and the activity behind it is usually retired afterwards. The fixture had
    /// no row of this shape, which is how the anchor grid came to count a row it could not project and
    /// render an empty page claiming one item.
    /// </summary>
    public const long MemoryAnchorRetiredHobby = 18_002;

    // --- routines -------------------------------------------------------------------------------
    // Item streaks in three states — running (12), just started (1), never (0) — and, from e2e-v4, four
    // more daily items at 45 / 120 / 200 / 400. The SPA's milestone icon reads the item's *current* streak
    // against 7 / 30 / 90 / 180 / 365, so before these only the lowest tier had ever rendered.
    public const long RoutineTakeMedication = 19_001;
    public const long RoutineWeeklyReview = 19_002;
    public const long RoutinePayRent = 19_003;

    /// <summary>Streak 45 — the ≥30 tier. The one daily item not yet ticked today, so the group is not all-done.</summary>
    public const long RoutineDrinkWater = 19_004;

    /// <summary>Streak 120 — the ≥90 tier.</summary>
    public const long RoutineJournal = 19_005;

    /// <summary>Streak 200 — the ≥180 tier.</summary>
    public const long RoutineMakeTheBed = 19_006;

    /// <summary>Streak 400 — the ≥365 tier.</summary>
    public const long RoutineBrushTeeth = 19_007;

    /// <summary>
    /// How many closed cycles the Daily and Weekly periods each carry in <c>routine_period_completions</c>.
    /// Equal to <c>RoutineTimePeriod.HistoryDepth</c>'s default, so the heatmap is full rather than
    /// left-padded; the rows have no fixture id of their own because nothing addresses one by id — the freeze
    /// action keys on <c>periodStart</c>.
    /// </summary>
    public const int RoutineHistoryCycles = 16;

    // --- day-planner templates ------------------------------------------------------------------
    // Three, not one: /day-planner/templates/split puts two templates side by side, so a fixture with a
    // single template leaves half that view unreachable in exactly the way an empty list does.
    public const long TemplateHomeOffice = 20_001;
    public const long TemplateOfficeDay = 20_002;

    /// <summary>Pinned, so the template list's pinned section is not empty either.</summary>
    public const long TemplateRelaxedWeekend = 20_003;

    /// <summary>Template tasks run 20_101–20_112, in the order the three templates are declared.</summary>
    public const long TemplateTaskFirst = 20_101;

    // --- tracking pattern mappings --------------------------------------------------------------
    /// <summary>
    /// Matches the one desktop process the ledger records at a full 60 active seconds per window, so the
    /// attribution the real ingest path performs is part of the fixture rather than a rule nothing exercises.
    /// </summary>
    public const long DesktopMappingRider = 21_001;

    /// <summary>
    /// An ignore rule. Its process appears nowhere in the ledger, which is not an oversight — the ingest
    /// path drops an entry an ignore rule matches, so a fixture holding both the rule and its rows would
    /// describe a state the pipeline cannot produce.
    /// </summary>
    public const long DesktopMappingIgnoredLauncher = 21_002;

    public const long AndroidMappingSpotify = 21_101;
    public const long AndroidMappingIgnoredSettings = 21_102;

    // --- notifications --------------------------------------------------------------------------
    /// <summary>
    /// The bell list: 22_001–22_006 for the primary account (three unread, then three read), 22_101–22_102
    /// for the secondary one. Six kinds rather than six copies of one, so the type filter discriminates.
    /// </summary>
    public const long NotificationFirst = 22_001;

    public const long SecondaryNotificationFirst = 22_101;

    // --- reminders ------------------------------------------------------------------------------
    // Two rows per reminder, in two projects: the portal's own Reminder (the user's intent) and the
    // module's ReminderDefinition (the scheduler state). They are kept in step by hand here rather than
    // by calling ReminderRegistrationService, for the reason E2eFixtureSeeder.AddRemindersAsync gives.

    /// <summary>Standalone, one-shot, two lead offsets. Fires inside the overview's 7-day window.</summary>
    public const long ReminderCallTheDentist = 23_001;

    /// <summary>Standalone, one-shot, a single offset. Also inside the 7-day window.</summary>
    public const long ReminderFileTaxReturn = 23_002;

    /// <summary>Standalone, recurring monthly. Deliberately due <i>outside</i> the window, so the overview's
    /// "next 7 days" count and its upcoming total are different numbers.</summary>
    public const long ReminderPayRent = 23_003;

    /// <summary>Attached to the tomorrow-09:00 planner task (<see cref="PlannerTaskFirst"/> + 3) — the
    /// task-linked flavour, whose instant is derived from the task rather than authored. A task still to
    /// come, because finishing or cancelling one retires its reminder. Paused, so the paused count is not
    /// zero.</summary>
    public const long ReminderStandup = 23_004;

    /// <summary>Cancelled, so the cancelled count is not zero either.</summary>
    public const long ReminderOldGymClass = 23_005;

    /// <summary>The secondary account's own reminder. The admin views span every user; with one account's
    /// rows only, nothing distinguishes them from the self-service view they mirror.</summary>
    public const long SecondaryReminderPostParcel = 23_101;

    /// <summary>
    /// The module-side definition for each reminder above, offset by 200 — <c>ReminderCallTheDentist + 200</c>
    /// is its definition. Not the same id as the portal row on purpose: two tables whose ids agree by
    /// coincidence invite a spec to use one where it means the other, and these routes take the definition id.
    /// </summary>
    public const long ReminderDefinitionOffset = 200;

    /// <summary>
    /// The dispatch ledger: 23_501–23_507, the last seven days of outcomes. Three <c>Sent</c>, one
    /// <c>Skipped</c>, and two <c>Failed</c> of which one is reversed by the seventh row — so the overview's
    /// "needs attention" count is 1 rather than 2, which is the whole point of the reversal lineage.
    /// <para>
    /// 500 rather than 300: the secondary account's definition is <c>23_101 + 200 = 23_301</c>, and two bands
    /// meeting at one number is a coincidence that reads as a mistake even though the tables differ.
    /// </para>
    /// </summary>
    public const long ReminderDispatchFirst = 23_501;
}
