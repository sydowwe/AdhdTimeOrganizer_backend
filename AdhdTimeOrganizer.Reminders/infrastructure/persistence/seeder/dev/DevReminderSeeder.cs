using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.reminders;
using MojaDigitalnaFirma.Kernel.user;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.seeder.dev;

public class DevReminderSeeder(DbContext dbContext, ISeedUserIdProvider seedUsers, ILogger<DevReminderSeeder> logger)
    : IAppWideDevSeeder, IScopedService
{
    public string SeederName => "Dev Reminders";
    public int Order => 71; // After employees (1) and users (5)

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ReminderDefinition>();
    }

    public async Task Seed()
    {
        if (await dbContext.Set<ReminderDefinition>().AnyAsync())
        {
            logger.LogInformation("Reminder definitions already exist, skipping.");
            return;
        }

        var userIds = await seedUsers.GetSeedUserIdsAsync(3);

        if (userIds.Count == 0)
        {
            logger.LogWarning("No users found. Skipping reminder seeding.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var primaryId = userIds[0];
        var secondaryId = userIds.Count > 1 ? userIds[1] : primaryId;

        // --- Definitions ---

        // 1 â€” OneShot: contract expiry, Active, next occurrence in 2 days (upcoming)
        var contractExpiry = new ReminderDefinition
        {
            OwnerModule = "Zmluvy",
            SubjectType = "Contract",
            SubjectId = "42",
            Kind = "ExpiryWarning",
            TemplateKey = "contract.expiry-warning",
            PayloadJson = """{"contractNumber":"ZML-2026-042","title":"DodÃ¡vka kancelÃ¡rskych potrieb"}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = now.AddDays(30),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddDays(2),
            // Targeted at an employee user (the contract gestor) so the employee-facing FE has an upcoming
            // deadline with multiple lead-time offsets to render.
            Recipients = [new ReminderRecipient { UserId = secondaryId }],
            LeadOffsets =
            [
                new ReminderLeadOffset { OffsetMinutes = -20160 }, // 14 days before
                new ReminderLeadOffset { OffsetMinutes = -2880 } // 2 days before
            ]
        };

        // 2 â€” OneShot: contract obligation overdue (NextOccurrenceAt in the past â€” scanner would pick this up).
        //     Targeted at an employee user so the next scan delivers a real notification to the employee
        //     (the only immediately-firing reminder for the employee-facing FE).
        var obligationOverdue = new ReminderDefinition
        {
            OwnerModule = "Zmluvy",
            SubjectType = "ContractObligation",
            SubjectId = "7",
            Kind = "ObligationDue",
            TemplateKey = "contract.obligation-due",
            PayloadJson = """{"contractNumber":"ZML-2026-010","obligationTitle":"ZaslaÅ¥ kvartÃ¡lny report"}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = now.AddHours(1),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddHours(-2), // overdue
            Recipients = [new ReminderRecipient { UserId = secondaryId }],
            LeadOffsets = [new ReminderLeadOffset { OffsetMinutes = -120 }]
        };

        // 3 â€” RecurringInterval: weekly stock review (Active)
        var weeklyStock = new ReminderDefinition
        {
            OwnerModule = "Inventory",
            SubjectType = "StockLevel",
            SubjectId = "global",
            Kind = "WeeklyStockReview",
            TemplateKey = "inventory.stock-review",
            PayloadJson = "{}",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.RecurringInterval,
            IntervalPreset = ReminderIntervalPreset.Weekly,
            AnchorDate = now.AddDays(-7),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddDays(1),
            LastOccurrenceAt = now.AddDays(-6),
            Recipients = [new ReminderRecipient { UserId = primaryId }]
        };

        // 4 â€” RecurringInterval: monthly HR events digest (Active, two recipients)
        var monthlyHr = new ReminderDefinition
        {
            OwnerModule = "EmployeeModule",
            SubjectType = "HrEvents",
            SubjectId = "monthly",
            Kind = "HrEventsDigest",
            TemplateKey = "hr.upcoming-events-digest",
            PayloadJson = """{"withinDays":7}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.RecurringInterval,
            IntervalPreset = ReminderIntervalPreset.Monthly,
            AnchorDate = now.AddMonths(-1),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddDays(5),
            LastOccurrenceAt = now.AddDays(-25),
            Recipients =
            [
                new ReminderRecipient { UserId = primaryId },
                new ReminderRecipient { UserId = secondaryId }
            ]
        };

        // 5 â€” RecurringCron: registratÃºra disposal check on the 1st of each month (Active)
        var disposalCron = new ReminderDefinition
        {
            OwnerModule = "Registratura",
            SubjectType = "DisposalCheck",
            SubjectId = "annual",
            Kind = "DisposalDue",
            TemplateKey = "registratura.disposal-due",
            PayloadJson = """{"eligibleSpisyCount":4}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.RecurringCron,
            Cron = "0 8 1 * *", // 08:00 on the 1st of each month (UTC)
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddDays(3),
            LastOccurrenceAt = now.AddDays(-28),
            Recipients = [new ReminderRecipient { UserId = primaryId }]
        };

        // 6 â€” Paused: travel settlement deadline (employee on leave, paused by HR)
        var pausedSettlement = new ReminderDefinition
        {
            OwnerModule = "CestovneNahrady",
            SubjectType = "TravelOrder",
            SubjectId = "88",
            Kind = "SettlementDeadline",
            TemplateKey = "travel.settlement-deadline",
            PayloadJson = """{"orderNumber":"CP-2026-088"}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = now.AddDays(10),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Paused,
            NextOccurrenceAt = null,
            Recipients = [new ReminderRecipient { UserId = secondaryId }],
            LeadOffsets = [new ReminderLeadOffset { OffsetMinutes = -1440 }] // 1 day before
        };

        // 7 â€” Cancelled: contract deleted before reminder fired
        var cancelledReminder = new ReminderDefinition
        {
            OwnerModule = "Zmluvy",
            SubjectType = "Contract",
            SubjectId = "13",
            Kind = "ExpiryWarning",
            TemplateKey = "contract.expiry-warning",
            PayloadJson = """{"contractNumber":"ZML-2025-013"}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = now.AddDays(-5),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Cancelled,
            NextOccurrenceAt = null,
            IsActive = false,
            Recipients = [new ReminderRecipient { UserId = primaryId }],
            LeadOffsets = [new ReminderLeadOffset { OffsetMinutes = -10080 }] // 7 days before
        };

        // 8 â€” Completed: one-shot fully dispatched
        var completedReminder = new ReminderDefinition
        {
            OwnerModule = "Zmluvy",
            SubjectType = "Contract",
            SubjectId = "5",
            Kind = "PublicationDeadline",
            TemplateKey = "contract.publication-deadline",
            PayloadJson = """{"contractNumber":"ZML-2025-005","title":"SprÃ¡va IT infraÅ¡truktÃºry"}""",
            NotificationType = NotificationType.DeadlineApproaching,
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = now.AddDays(-10),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Completed,
            NextOccurrenceAt = null,
            CompletedAt = now.AddDays(-10),
            LastOccurrenceAt = now.AddDays(-17),
            Recipients = [new ReminderRecipient { UserId = primaryId }],
            LeadOffsets = [new ReminderLeadOffset { OffsetMinutes = -10080 }]
        };

        // 9 â€” RecurringInterval + ResolverStrategy: daily leave-approval digest resolved to the subject's
        //      manager at dispatch time. Exercises the resolver-key recipient path (no explicit Recipients),
        //      the custom-renderer branch (NotificationType == null), and EndsAt / DigestKey / ChannelHint.
        var resolverDigest = new ReminderDefinition
        {
            OwnerModule = "Attendance",
            SubjectType = "LeaveApprovals",
            SubjectId = "pending",
            Kind = "LeaveApprovalDigest",
            TemplateKey = "attendance.leave-approval-digest",
            PayloadJson = """{"pendingCount":3}""",
            NotificationType = null, // custom renderer supplies the text
            ScheduleType = ReminderScheduleType.RecurringInterval,
            IntervalPreset = ReminderIntervalPreset.Daily,
            AnchorDate = now.AddDays(-3),
            EndsAt = now.AddMonths(3),
            RecipientMode = RecipientMode.ResolverStrategy,
            RecipientResolverKey = "subject-manager",
            DigestKey = "attendance.leave-approvals.daily",
            ChannelHint = NotificationChannel.WebPush,
            Status = ReminderStatus.Active,
            NextOccurrenceAt = now.AddHours(6),
            LastOccurrenceAt = now.AddDays(-1)
        };

        var definitions = new List<ReminderDefinition>
        {
            contractExpiry, obligationOverdue, weeklyStock, monthlyHr,
            disposalCron, pausedSettlement, cancelledReminder, completedReminder,
            resolverDigest
        };

        dbContext.AddRange(definitions);
        await dbContext.SaveChangesAsync();

        // --- Dispatch history (after definitions are saved so IDs are available) ---

        // Weekly stock review â€” last week's successful dispatch. Kept as a local so its EF-assigned Id can be
        // read after the first save and referenced by the Reversed correction row in the second save below.
        var weeklyStockSent = new ReminderDispatch
        {
            ReminderDefinitionId = weeklyStock.Id,
            OccurrenceAt = now.AddDays(-6),
            DispatchedAt = now.AddDays(-6).AddMinutes(1),
            Outcome = DispatchOutcome.Sent,
            NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
            TemplateKeySnapshot = "inventory.stock-review",
            RecipientsSnapshot = $"[{primaryId}]",
            CorrelationId = Guid.NewGuid().ToString()
        };

        var dispatches = new List<ReminderDispatch>
        {
            // Completed contract publication â€” the dispatch that finalised it
            new()
            {
                ReminderDefinitionId = completedReminder.Id,
                OccurrenceAt = now.AddDays(-17),
                DispatchedAt = now.AddDays(-17).AddMinutes(1),
                Outcome = DispatchOutcome.Sent,
                NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
                TemplateKeySnapshot = "contract.publication-deadline",
                RecipientsSnapshot = $"[{primaryId}]",
                CorrelationId = Guid.NewGuid().ToString()
            },

            weeklyStockSent,

            // Weekly stock review â€” the week before, skipped because no recipients resolved
            new()
            {
                ReminderDefinitionId = weeklyStock.Id,
                OccurrenceAt = now.AddDays(-13),
                DispatchedAt = now.AddDays(-13).AddMinutes(1),
                Outcome = DispatchOutcome.Skipped,
                SkipReason = SkipReason.NoRecipients,
                NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
                TemplateKeySnapshot = "inventory.stock-review",
                RecipientsSnapshot = "[]",
                CorrelationId = Guid.NewGuid().ToString()
            },

            // Monthly HR digest â€” last month's dispatch
            new()
            {
                ReminderDefinitionId = monthlyHr.Id,
                OccurrenceAt = now.AddDays(-25),
                DispatchedAt = now.AddDays(-25).AddMinutes(2),
                Outcome = DispatchOutcome.Sent,
                NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
                TemplateKeySnapshot = "hr.upcoming-events-digest",
                RecipientsSnapshot = $"[{primaryId},{secondaryId}]",
                CorrelationId = Guid.NewGuid().ToString()
            },

            // Monthly HR digest â€” the month before, dispatch errored (logged, not silently dropped)
            new()
            {
                ReminderDefinitionId = monthlyHr.Id,
                OccurrenceAt = now.AddDays(-55),
                DispatchedAt = now.AddDays(-55).AddMinutes(2),
                Outcome = DispatchOutcome.Failed,
                NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
                TemplateKeySnapshot = "hr.upcoming-events-digest",
                RecipientsSnapshot = $"[{primaryId},{secondaryId}]",
                CorrelationId = Guid.NewGuid().ToString()
            }
        };

        // Step 1: save the dispatches; EF populates weeklyStockSent.Id after this call.
        dbContext.AddRange(dispatches);
        await dbContext.SaveChangesAsync();

        // Step 2: a correction that reverses the weekly stock Sent row (sent in error â€” wrong stock snapshot).
        // Corrections are append-only reversal rows linked via ReversesDispatchId, never updates/deletes.
        var weeklyStockReversal = new ReminderDispatch
        {
            ReminderDefinitionId = weeklyStock.Id,
            OccurrenceAt = weeklyStockSent.OccurrenceAt,
            DispatchedAt = now.AddDays(-6).AddHours(2),
            Outcome = DispatchOutcome.Reversed,
            ReversesDispatchId = weeklyStockSent.Id,
            NotificationTypeSnapshot = NotificationType.DeadlineApproaching,
            TemplateKeySnapshot = "inventory.stock-review",
            RecipientsSnapshot = $"[{primaryId}]",
            CorrelationId = Guid.NewGuid().ToString()
        };
        dbContext.Add(weeklyStockReversal);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Seeded {DefCount} reminder definitions and {DispatchCount} dispatch rows (incl. reversal).",
            definitions.Count, dispatches.Count + 1);
    }
}