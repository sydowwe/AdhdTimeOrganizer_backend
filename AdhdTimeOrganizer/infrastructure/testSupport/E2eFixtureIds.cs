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
/// auto-generated set is <c>calendar</c> — roughly 730 rows per user for three users, so about 2 200
/// after a reset. Everything here starts at 10 000. <see cref="E2eFixtureSeeder"/> advances each
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
    /// </remarks>
    public const string FixtureVersion = "e2e-v2";

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

    // --- routines -------------------------------------------------------------------------------
    public const long RoutineTakeMedication = 19_001;
    public const long RoutineWeeklyReview = 19_002;
    public const long RoutinePayRent = 19_003;

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
}
