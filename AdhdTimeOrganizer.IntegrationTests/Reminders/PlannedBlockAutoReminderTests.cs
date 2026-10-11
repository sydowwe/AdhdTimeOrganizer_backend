using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Reminders.application.job;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// P1-03 / audit N1: a new planned block gets a reminder of its own under the user's <c>AutoReminderMode</c>, and
/// the repeating rules' occurrences reach the plan — and so get theirs — without anyone opening the app. Asserted
/// on rows on both sides of the seam, because each half fails silently:
/// <list type="bullet">
/// <item><b>Every creation path</b> leaves an <c>IsAuto</c> portal <c>Reminder</c> <i>and</i> an active module
/// <c>ReminderDefinition</c> of the automatic kind — or neither, when the mode says no. A path that forgets
/// answers 201 and the block simply never rings.</item>
/// <item><b>Created once.</b> A deleted automatic reminder is not re-created by a move or a status change, and an
/// edited one becomes the user's own.</item>
/// <item><b>The sweep runs unauthenticated</b>, where nothing can stamp a <c>UserId</c>: the occurrence and its
/// reminder must still carry the owner's.</item>
/// <item><b>The guard</b> retires an occurrence whose block was started or moved behind the sync's back.</item>
/// </list>
/// Real clock; blocks are days ahead so a run never straddles one. The fixture user is on UTC.
/// </summary>
[Collection("Postgres")]
public class PlannedBlockAutoReminderTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly Tomorrow => Today.AddDays(1);
    private static DateOnly BlockDay => Today.AddDays(3);
    private static readonly TimeOnly Noon = new(12, 0);

    private static readonly string[] EveryDay =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), TimeSpan.Zero);

    // ---- the plain create, and the mode --------------------------------------------------------------

    [Fact(DisplayName = "N1: by default a new Critical block gets one automatic reminder, 10 min ahead, of its own kind")]
    public async Task Create_CriticalBlock_UnderTheDefaults_GetsAnAutomaticReminder()
    {
        var activityId = await ActivityAsync("deep work");
        var taskId = await CreateBlockAsync(CreateClient(), activityId, BlockDay, Noon, importanceId: await ImportanceAsync(TaskImportance.CriticalMarkerValue));

        var reminder = await AutoReminderAsync(taskId);
        reminder.Should().NotBeNull("the defaults are ImportantOnly from Critical up");
        reminder!.UserId.Should().Be(UserId);
        reminder.Title.Should().Be("deep work", "an automatic reminder is titled with the block's activity");
        reminder.RemindAt.Should().Be(Utc(BlockDay, Noon).UtcDateTime);
        reminder.LeadOffsetsMinutes.Should().Equal(-10);

        var definition = await DefinitionAsync(reminder.Id);
        definition.Should().NotBeNull();
        definition!.Status.Should().Be(ReminderStatus.Active);
        definition.NotificationType.Should().Be(NotificationType.PlannedBlockStarting);
        definition.TemplateKey.Should().Be(ReminderRegistrationService.AutoTemplateKey);
        definition.NextOccurrenceAt.Should().Be(Utc(BlockDay, Noon).AddMinutes(-10));
        definition.Recipients.Select(r => r.UserId).Should().Equal(UserId);

        (await PersonalDefinitionCountAsync(reminder.Id)).Should().Be(0, "it is not registered as a personal reminder too");
    }

    [Theory(DisplayName = "N1: the mode decides which new blocks get one")]
    [InlineData("Off", 999, 0, false, false)]
    [InlineData("ImportantOnly", null, 0, false, false)]
    [InlineData("ImportantOnly", 500, 999, false, false)]
    [InlineData("ImportantOnly", 500, 400, false, true)]
    [InlineData("ImportantOnly", 666, 100, false, false)]
    [InlineData("All", 666, 999, false, true)]
    [InlineData("All", null, 999, false, true)]
    [InlineData("All", null, 999, true, false)]
    public async Task Create_TheModeDecides(string mode, int? importance, int minImportance, bool isBackground, bool expected)
    {
        var client = CreateClient();
        await PutSettingsAsync(client, mode, minImportance);

        var importanceId = importance is { } value ? await ImportanceAsync(value) : (long?)null;
        var taskId = await CreateBlockAsync(client, await ActivityAsync($"mode-{mode}"), BlockDay, Noon, importanceId, isBackground);

        if (expected)
            (await DefinitionAsync((await AutoReminderAsync(taskId))!.Id))!.Status.Should().Be(ReminderStatus.Active);
        else
            (await RemindersOfAsync(taskId)).Should().BeEmpty();
    }

    [Fact(DisplayName = "N1: a block that has already started, or is created started, gets none")]
    public async Task Create_PastOrStarted_GetsNone()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");

        var past = await CreateBlockAsync(client, await ActivityAsync("yesterday"), Today.AddDays(-1), Noon);
        var started = await CreateBlockAsync(client, await ActivityAsync("already going"), BlockDay, Noon, status: "InProgress");

        (await RemindersOfAsync(past)).Should().BeEmpty();
        (await RemindersOfAsync(started)).Should().BeEmpty();
    }

    [Fact(DisplayName = "N1: a block created inside its lead window is reminded at its start instead of not at all")]
    public async Task Create_InsideTheLeadWindow_FiresAtTheStart()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");

        var start = DateTime.UtcNow.AddMinutes(6);
        if (DateOnly.FromDateTime(start) != Today)
            return; // the six minutes before UTC midnight: the block would land on another day than the one named

        var taskId = await CreateBlockAsync(client, await ActivityAsync("bus"), Today, new TimeOnly(start.Hour, start.Minute),
            end: new TimeOnly(23, 59));

        (await AutoReminderAsync(taskId))!.LeadOffsetsMinutes.Should().Equal(0);
    }

    [Fact(DisplayName = "N1: Off survives the first settings write — the database default does not override it")]
    public async Task Settings_OffOnTheFirstWrite_IsStored()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "Off", 123);

        await using var db = CreateDbContext();
        var row = await db.Set<UserPlannerSettings>().AsNoTracking().SingleAsync(s => s.UserId == UserId, CancellationToken);
        row.AutoReminderMode.Should().Be(AutoReminderMode.Off);
        row.AutoReminderMinImportance.Should().Be(123);
    }

    // ---- the other creation paths --------------------------------------------------------------------

    [Fact(DisplayName = "N1: applying a template gives each new block its automatic reminder")]
    public async Task ApplyTemplate_EachNewBlockGetsOne()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var activityId = await ActivityAsync("template line");

        long templateId, calendarId;
        await using (var db = CreateDbContext())
        {
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, BlockDay, ct: CancellationToken);
        }

        var response = await client.PostAsJsonAsync("api/calendar/apply-planner-template", new
        {
            TemplateId = templateId,
            CalendarId = calendarId,
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[] { TemplateLine(activityId, 9), TemplateLine(activityId, 14) }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));

        await AssertEveryBlockOnTheDayHasOneAsync(calendarId, expectedBlocks: 2);
    }

    [Fact(DisplayName = "N1: the batched template apply gives every new block on every day its automatic reminder")]
    public async Task ApplyTemplateBatch_EveryDaysBlocksGetOne()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var activityId = await ActivityAsync("batch line");

        long templateId, firstDay, secondDay;
        await using (var db = CreateDbContext())
        {
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);
            firstDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, BlockDay, ct: CancellationToken);
            secondDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, BlockDay.AddDays(1), ct: CancellationToken);
        }

        var response = await client.PostAsJsonAsync("api/calendar/apply-planner-template/batch", new
        {
            TemplateId = templateId,
            CalendarIds = new[] { firstDay, secondDay },
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[] { TemplateLine(activityId, 9) }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        await AssertEveryBlockOnTheDayHasOneAsync(firstDay, expectedBlocks: 1);
        await AssertEveryBlockOnTheDayHasOneAsync(secondDay, expectedBlocks: 1);
    }

    [Fact(DisplayName = "N1: a copied block gets its own automatic reminder, decided by the mode, not carried from the source")]
    public async Task CopyToDays_CopiesGetTheirOwn()
    {
        var client = CreateClient();
        long sourceDay, targetDay, sourceTaskId;
        await using (var db = CreateDbContext())
        {
            sourceDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, BlockDay, ct: CancellationToken);
            targetDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, BlockDay.AddDays(1), ct: CancellationToken);
            // Seeded straight into the table: the source has no reminder of any kind.
            sourceTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, await PlanningTestSeedHelper.SeedActivityAsync(db, "copied", ct: CancellationToken),
                sourceDay, Noon, new TimeOnly(13, 0), ct: CancellationToken);
        }

        await PutSettingsAsync(client, "All");
        var response = await client.PostAsJsonAsync("api/planner-task/copy-to-days",
            new { SourceCalendarId = sourceDay, TargetCalendarIds = new[] { targetDay } }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        (await RemindersOfAsync(sourceTaskId)).Should().BeEmpty("the source block was not created by this request");
        await AssertEveryBlockOnTheDayHasOneAsync(targetDay, expectedBlocks: 1);
    }

    [Fact(DisplayName = "N1: a repeating rule's occurrence placed by the day-plan read gets its automatic reminder")]
    public async Task MaterialiserRead_OccurrenceGetsOne()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var ruleId = await SeedRuleAsync(UserId);

        var read = await client.GetAsync($"api/calendar/day-plan/{Today:yyyy-MM-dd}", CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(CancellationToken));

        var tomorrows = await OccurrenceAsync(ruleId, Tomorrow);
        var reminder = await AutoReminderAsync(tomorrows.Id);
        reminder.Should().NotBeNull("tomorrow's occurrence always starts in the future");
        (await DefinitionAsync(reminder!.Id))!.Status.Should().Be(ReminderStatus.Active);
    }

    [Fact(DisplayName = "N1: an occurrence with only an automatic reminder still follows a rule edit, and its reminder moves with it")]
    public async Task MaterialiserReconcile_AutoReminderDoesNotPinTheOccurrence()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var activityId = await ActivityAsync("evening run");
        var importanceId = await ImportanceAsync(500);
        var ruleId = await CreateRuleAsync(client, activityId, importanceId, startHour: 18);

        var before = await OccurrenceAsync(ruleId, Tomorrow);
        var reminderId = (await AutoReminderAsync(before.Id))!.Id;

        var put = await client.PutAsJsonAsync($"api/repeating-planner-task/{ruleId}", RuleBody(activityId, importanceId, 20), JsonOpts, CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(CancellationToken));

        var after = await OccurrenceAsync(ruleId, Tomorrow);
        after.Id.Should().Be(before.Id, "an untouched occurrence is edited in place");
        after.StartTime.Should().Be(new TimeOnly(20, 0), "an automatic reminder is not the user touching the block");

        (await DefinitionAsync(reminderId))!.NextOccurrenceAt.Should().Be(Utc(Tomorrow, new TimeOnly(20, 0)).AddMinutes(-10),
            "the occurrence moved, so its reminder re-syncs to the new start");
    }

    // ---- created once ---------------------------------------------------------------------------------

    [Fact(DisplayName = "N1: a deleted automatic reminder is not re-created by a later move or status change")]
    public async Task DeletedAutoReminder_StaysDeleted()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var taskId = await CreateBlockAsync(client, await ActivityAsync("no thanks"), BlockDay, Noon);
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        (await client.DeleteAsync($"api/reminder/{reminderId}", CancellationToken)).IsSuccessStatusCode.Should().BeTrue();

        var move = await client.PatchAsJsonAsync($"api/planner-task/{taskId}",
            new { StartTime = new { Hours = 15, Minutes = 0 }, EndTime = new { Hours = 16, Minutes = 0 } }, JsonOpts, CancellationToken);
        move.IsSuccessStatusCode.Should().BeTrue(await move.Content.ReadAsStringAsync(CancellationToken));
        var status = await client.PatchAsJsonAsync($"api/planner-task/{taskId}/status",
            new { status = "NotStarted", actualStartTime = (object?)null, actualEndTime = (object?)null }, JsonOpts, CancellationToken);
        status.IsSuccessStatusCode.Should().BeTrue(await status.Content.ReadAsStringAsync(CancellationToken));

        (await RemindersOfAsync(taskId)).Should().BeEmpty("the user deleted it, and only a create makes one");
        (await DefinitionAsync(reminderId))!.Status.Should().Be(ReminderStatus.Cancelled);
    }

    [Fact(DisplayName = "N1: moving the block moves its automatic reminder")]
    public async Task MovedBlock_MovesItsReminder()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var taskId = await CreateBlockAsync(client, await ActivityAsync("moved block"), BlockDay, Noon);
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        var move = await client.PatchAsJsonAsync($"api/planner-task/{taskId}",
            new { StartTime = new { Hours = 15, Minutes = 0 }, EndTime = new { Hours = 16, Minutes = 0 } }, JsonOpts, CancellationToken);
        move.IsSuccessStatusCode.Should().BeTrue(await move.Content.ReadAsStringAsync(CancellationToken));

        var definitions = await AllDefinitionsAsync(reminderId);
        definitions.Should().ContainSingle();
        definitions[0].NextOccurrenceAt.Should().Be(Utc(BlockDay, new TimeOnly(15, 0)).AddMinutes(-10));
    }

    /// <summary>
    /// The cascade takes the block and its <c>Reminder</c> row; <c>PlannerTaskActivityRemovalParticipant</c> only has
    /// the gone row's id, so it is <c>CancelAsync</c> withdrawing both kinds' keys that reaches the automatic one.
    /// </summary>
    [Fact(DisplayName = "N1: deleting the block's activity withdraws its automatic reminder")]
    public async Task DeletedActivity_WithdrawsTheAutoReminder()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var activityId = await ActivityAsync("deleted with its activity");
        var taskId = await CreateBlockAsync(client, activityId, BlockDay, Noon);
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        (await client.DeleteAsync($"api/activity/{activityId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RemindersOfAsync(taskId)).Should().BeEmpty("the cascade took the block and its reminder");
        (await DefinitionAsync(reminderId))!.Status.Should().Be(ReminderStatus.Cancelled,
            "otherwise it sits on my-upcoming and rings for a block that no longer exists");
    }

    [Fact(DisplayName = "N1: an automatic reminder the user edits becomes theirs — a personal reminder, the automatic one withdrawn")]
    public async Task EditedAutoReminder_BecomesTheUsersOwn()
    {
        var client = CreateClient();
        await PutSettingsAsync(client, "All");
        var taskId = await CreateBlockAsync(client, await ActivityAsync("edited"), BlockDay, Noon);
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        var put = await client.PutAsJsonAsync($"api/reminder/{reminderId}", new
        {
            Title = "Bring the charger",
            RemindAt = Utc(BlockDay, Noon).UtcDateTime,
            LeadOffsetsMinutes = new[] { -30 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(CancellationToken));

        await using var db = CreateDbContext();
        var row = await db.Set<Reminder>().AsNoTracking().SingleAsync(r => r.Id == reminderId, CancellationToken);
        row.IsAuto.Should().BeFalse();
        row.Title.Should().Be("Bring the charger");

        (await DefinitionAsync(reminderId))!.Status.Should().Be(ReminderStatus.Cancelled, "it would otherwise fire twice");
        (await PersonalDefinitionCountAsync(reminderId, activeOnly: true)).Should().Be(1);
    }

    // ---- the sweep: no signed-in user ------------------------------------------------------------------

    [Fact(DisplayName = "N1 × FO6: the sweep, unauthenticated, places tomorrow's occurrence and its reminder with the owner's UserId")]
    public async Task Sweep_Unauthenticated_PlacesWithTheOwnersUserId()
    {
        var ruleId = await SeedRuleAsync(UserId);

        await using var factory = CreateBackgroundFactory();
        await RunSweepAsync(factory);

        var occurrence = await OccurrenceAsync(ruleId, Tomorrow);
        occurrence.UserId.Should().Be(UserId, "nobody was signed in to stamp it, so it must have been written explicitly");

        var reminder = await AutoReminderAsync(occurrence.Id);
        reminder.Should().NotBeNull("with no settings row the defaults apply, and the rule's block is Critical");
        reminder!.UserId.Should().Be(UserId);
        (await DefinitionAsync(reminder.Id))!.Recipients.Select(r => r.UserId).Should().Equal(UserId);

        await using var db = CreateDbContext();
        (await db.Set<RepeatingPlannerTask>().AsNoTracking().SingleAsync(r => r.Id == ruleId, CancellationToken))
            .MaterialisedThrough.Should().Be(Tomorrow);
    }

    [Fact(DisplayName = "N1 × FO6: the sweep skips an away user and leaves their days undecided for their return")]
    public async Task Sweep_SkipsAnAwayUser()
    {
        var ruleId = await SeedRuleAsync(UserId);
        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
            user.LastActiveAt = DateTime.UtcNow - UserPresence.QuietAfter - TimeSpan.FromHours(1);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateBackgroundFactory();
        await RunSweepAsync(factory);

        await using var verify = CreateDbContext();
        (await verify.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.SourceRepeatingTaskId == ruleId, CancellationToken))
            .Should().BeFalse();
        (await verify.Set<RepeatingPlannerTask>().AsNoTracking().SingleAsync(r => r.Id == ruleId, CancellationToken))
            .MaterialisedThrough.Should().BeNull("a skipped user's days are not decided, so their first read places them");
    }

    // ---- the scan: delivery and the guard --------------------------------------------------------------

    [Fact(DisplayName = "N1: at the lead the scan sends PlannedBlockStarting, linked to the block")]
    public async Task Scan_Fires()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var taskId = await CreateBlockAsync(factory.CreateClient(), await ActivityAsync("fires"), BlockDay, Noon,
            importanceId: await ImportanceAsync(TaskImportance.CriticalMarkerValue));

        await FireScanAsync(factory, Utc(BlockDay, Noon).AddMinutes(-9));

        await using var db = CreateDbContext();
        var notification = await db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.UserId == UserId && n.Type == NotificationType.PlannedBlockStarting, CancellationToken);
        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("plannerTaskId").GetInt64().Should().Be(taskId);
        payload.GetProperty("minutesBefore").GetInt32().Should().Be(10);
        payload.GetProperty("title").GetString().Should().Be("fires");
    }

    [Fact(DisplayName = "N1: a block started behind the sync's back is not announced — retired at fire time")]
    public async Task Scan_StartedBehindTheSyncsBack_IsRetired()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var taskId = await CreateBlockAsync(factory.CreateClient(), await ActivityAsync("started early"), BlockDay, Noon,
            importanceId: await ImportanceAsync(TaskImportance.CriticalMarkerValue));
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        await using (var db = CreateDbContext())
            await db.Set<PlannerTask>().Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, PlannerTaskStatus.InProgress), CancellationToken);

        await FireScanAsync(factory, Utc(BlockDay, Noon).AddMinutes(-9));

        await AssertRetiredAsync(reminderId);
    }

    [Fact(DisplayName = "N1: a block moved behind the sync's back is retired — the stale start never rings")]
    public async Task Scan_MovedBehindTheSyncsBack_IsRetired()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var taskId = await CreateBlockAsync(factory.CreateClient(), await ActivityAsync("moved quietly"), BlockDay, Noon,
            importanceId: await ImportanceAsync(TaskImportance.CriticalMarkerValue));
        var reminderId = (await AutoReminderAsync(taskId))!.Id;

        await using (var db = CreateDbContext())
            await db.Set<PlannerTask>().Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartTime, new TimeOnly(18, 0)), CancellationToken);

        await FireScanAsync(factory, Utc(BlockDay, Noon).AddMinutes(-9));

        await AssertRetiredAsync(reminderId);
    }

    [Fact(DisplayName = "N1: automatic block reminders can be muted as a kind before one exists")]
    public async Task KindCatalog_OffersTheKind()
    {
        var response = await CreateClient().GetAsync("api/reminder-preference", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var kinds = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken)).GetProperty("kinds");
        kinds.EnumerateArray().Should().Contain(k =>
            k.GetProperty("ownerModule").GetString() == ReminderRegistrationService.OwnerModule
            && k.GetProperty("kind").GetString() == ReminderRegistrationService.AutoKind
            && k.GetProperty("enabled").GetBoolean());
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private async Task<long> ActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
    }

    /// <summary>The level with this value, created if the user has none — <c>(user_id, importance)</c> is unique.</summary>
    private async Task<long> ImportanceAsync(int value)
    {
        await using var db = CreateDbContext();
        var existing = await db.Set<TaskImportance>().IgnoreQueryFilters()
            .Where(i => i.UserId == UserId && i.Importance == value).Select(i => (long?)i.Id).FirstOrDefaultAsync(CancellationToken);
        return existing ?? await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, value, UserId, ct: CancellationToken);
    }

    private async Task<long> CreateBlockAsync(HttpClient client, long activityId, DateOnly date, TimeOnly start,
        long? importanceId = null, bool isBackground = false, string status = "NotStarted", TimeOnly? end = null)
    {
        var until = end ?? start.AddHours(1);
        var response = await client.PostAsJsonAsync("api/planner-task", new
        {
            StartTime = new { Hours = start.Hour, Minutes = start.Minute },
            EndTime = new { Hours = until.Hour, Minutes = until.Minute },
            IsBackground = isBackground,
            ActivityId = activityId,
            ImportanceId = importanceId,
            Status = status,
            Date = date
        }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return long.Parse(body);
    }

    private async Task PutSettingsAsync(HttpClient client, string mode, int minImportance = TaskImportance.CriticalMarkerValue)
    {
        var response = await client.PutAsJsonAsync("api/planner/settings", new
        {
            RemindersEnabled = true,
            ReminderMinutesBefore = 10,
            AutoReminderMode = mode,
            AutoReminderMinImportance = minImportance,
            DetailsPanelExpandedByDefault = true,
            ArrowKeyNavEnabled = true,
            PredefinedSkipReasons = Array.Empty<string>(),
            SlotDurationMinutes = 15,
            DefaultConflictResolution = "Ignore",
            DefaultApplyPreviewMode = true
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static object TemplateLine(long activityId, int startHour) => new
    {
        StartTime = new { Hours = startHour, Minutes = 0 },
        EndTime = new { Hours = startHour + 1, Minutes = 0 },
        IsBackground = false,
        ActivityId = activityId,
        Status = "NotStarted"
    };

    private static object RuleBody(long activityId, long importanceId, int startHour) => new
    {
        ActivityId = activityId,
        ImportanceId = importanceId,
        StartTime = new { Hours = startHour, Minutes = 0 },
        EndTime = new { Hours = startHour + 1, Minutes = 0 },
        IsBackground = false,
        IsActive = true,
        RecurrenceType = "DayOfWeek",
        ScheduledDays = EveryDay,
        AutoPlace = true
    };

    private async Task<long> CreateRuleAsync(HttpClient client, long activityId, long importanceId, int startHour)
    {
        var response = await client.PostAsJsonAsync("api/repeating-planner-task", RuleBody(activityId, importanceId, startHour), JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return long.Parse(body);
    }

    /// <summary>An every-day, auto-placing, Critical rule written straight to the table — nothing placed yet.</summary>
    private async Task<long> SeedRuleAsync(long userId)
    {
        var importanceId = await ImportanceAsync(TaskImportance.CriticalMarkerValue);
        await using var db = CreateDbContext();
        var rule = new RepeatingPlannerTask
        {
            UserId = userId,
            ActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"n1-rule-{Guid.NewGuid():N}", userId, CancellationToken),
            ImportanceId = importanceId,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 0),
            IsBackground = false,
            IsActive = true,
            RecurrenceType = RecurrenceType.DayOfWeek,
            ScheduledDays = EveryDay.ToList(),
            AutoPlace = true
        };
        db.Add(rule);
        await db.SaveChangesAsync(CancellationToken);
        return rule.Id;
    }

    private async Task<PlannerTask> OccurrenceAsync(long ruleId, DateOnly date)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.SourceRepeatingTaskId == ruleId && t.Calendar.Date == date, CancellationToken);
    }

    private async Task AssertEveryBlockOnTheDayHasOneAsync(long calendarId, int expectedBlocks)
    {
        await using var db = CreateDbContext();
        var taskIds = await db.Set<PlannerTask>().AsNoTracking().Where(t => t.CalendarId == calendarId)
            .Select(t => t.Id).ToListAsync(CancellationToken);
        taskIds.Should().HaveCount(expectedBlocks);

        foreach (var taskId in taskIds)
        {
            var reminder = await AutoReminderAsync(taskId);
            reminder.Should().NotBeNull($"block {taskId} was created by this request");
            (await DefinitionAsync(reminder!.Id))!.Status.Should().Be(ReminderStatus.Active);
        }
    }

    private async Task<List<Reminder>> RemindersOfAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<Reminder>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.PlannerTaskId == taskId).ToListAsync(CancellationToken);
    }

    private async Task<Reminder?> AutoReminderAsync(long taskId) =>
        (await RemindersOfAsync(taskId)).Should().ContainSingle().Which is { IsAuto: true } reminder ? reminder : null;

    private async Task<ReminderDefinition?> DefinitionAsync(long reminderId) =>
        (await AllDefinitionsAsync(reminderId)).SingleOrDefault();

    private async Task<List<ReminderDefinition>> AllDefinitionsAsync(long reminderId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ReminderDefinition>().AsNoTracking().IgnoreQueryFilters()
            .Include(d => d.Recipients)
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.AutoKind)
            .ToListAsync(CancellationToken);
    }

    private async Task<int> PersonalDefinitionCountAsync(long reminderId, bool activeOnly = false)
    {
        await using var db = CreateDbContext();
        return await db.Set<ReminderDefinition>().AsNoTracking().IgnoreQueryFilters()
            .CountAsync(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                             && d.SubjectType == ReminderRegistrationService.SubjectType
                             && d.SubjectId == reminderId.ToString()
                             && d.Kind == ReminderRegistrationService.Kind
                             && (!activeOnly || d.Status == ReminderStatus.Active), CancellationToken);
    }

    private async Task AssertRetiredAsync(long reminderId)
    {
        var definition = await DefinitionAsync(reminderId);
        await using var db = CreateDbContext();
        var dispatch = await db.Set<ReminderDispatch>().AsNoTracking()
            .SingleAsync(d => d.ReminderDefinitionId == definition!.Id, CancellationToken);
        dispatch.Outcome.Should().Be(DispatchOutcome.Skipped);
        dispatch.SkipReason.Should().Be(SkipReason.SubjectRetired);

        (await db.Set<Notification>().AnyAsync(n => n.Type == NotificationType.PlannedBlockStarting, CancellationToken))
            .Should().BeFalse("a retired occurrence sends nothing");
    }

    private async Task FireScanAsync(ITestClientFactory factory, DateTimeOffset fireTime)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var scan = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == ReminderScanJobHandler.HandlerKey);

        await scan.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = fireTime.UtcDateTime,
            ActualFireTimeUtc = fireTime.UtcDateTime,
            JobKey = ReminderScanJobHandler.HandlerKey,
            CorrelationId = "planned-block-reminder-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    /// <summary>A host with nobody signed in — the Scheduler's dispatcher's situation, not a request's.</summary>
    private ITestClientFactory CreateBackgroundFactory() =>
        CreateFactory(TestRoles.AdminAndUser,
            configureServices: services => services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>(),
            configOverrides: EmailOff);

    private async Task RunSweepAsync(ITestClientFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == MaterialiseRepeatingTasksJobHandler.HandlerKey);

        await sweep.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = DateTime.UtcNow,
            ActualFireTimeUtc = DateTime.UtcNow,
            JobKey = MaterialiseRepeatingTasksJobHandler.HandlerKey,
            CorrelationId = "materialise-sweep-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private sealed class BackgroundLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No logged user in a background context.");
        public long GetUserId => throw new InvalidOperationException("No logged user in a background context.");
        public long? GetUserIdOrNull => null;
    }
}
