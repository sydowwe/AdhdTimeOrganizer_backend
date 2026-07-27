using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Core.EmployeeModule.Contracts.domain.serviceContract;
using MojaDigitalnaFirma.Core.EmployeeModule.domain.model.entity.employee;
using MojaDigitalnaFirma.Core.Reminders.domain.entity;
using MojaDigitalnaFirma.Core.Reminders.domain.@enum;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.config.dependencyInjection;

namespace MojaDigitalnaFirma.Core.infrastructure.service;

// Supplies the Reminders slice of an employee's GDPR Art. 15 / 20 export (reminders follow-up 02, review L3).
//
// WHY IT LIVES IN THE COMPOSITION PROJECT, not in Core.Reminders: the Reminders module deliberately
// references only Kernel + Framework — it imports NO domain module — and IEmployeePersonalDataProvider is
// an EmployeeModule.Contracts type. Implementing it inside Core.Reminders would give the module its first
// domain-contract dependency, and resolving employeeId → UserId needs the Employee entity itself, which is
// a full domain reference. MojaDigitalnaFirma.Core already composes both, so the adapter sits here and both
// modules stay as they were. (The mirror-image of the Zmluvy → Reminders producer seam wired in
// CoreServiceExtensions.) Registered by the IScopedService scan; EmployeeDataExportService composes every
// IEmployeePersonalDataProvider, so the Employee module still never references Reminders.
//
// Reminders are keyed by UserId, the export by EmployeeId — this bridges the two via Employee.UserId.
//
// PII discipline (mirrors MyUpcomingReminderDto): ids + schedule metadata only. The owner-supplied
// PayloadJson is EXCLUDED — it is opaque free text that can carry third-party PII (see its [AuditIgnore]
// note), and it is not this subject's data. Co-recipients are never listed, so no other user's ids leak.
public class ReminderPersonalDataProvider(DbContext dbContext)
    : IEmployeePersonalDataProvider, IScopedService
{
    public string SectionKey => "reminders";

    public async Task<object?> GetPersonalDataAsync(long employeeId, CancellationToken ct)
    {
        var userId = await dbContext.Set<Employee>()
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.UserId)
            .FirstOrDefaultAsync(ct);

        // 0 is the "no authenticated user" sentinel the user-scoped write path guards against — never a real id.
        if (userId == 0)
            return null;

        // Only EXPLICIT recipient rows. Resolver-strategy reminders are excluded by construction: their
        // recipients are unknown until dispatch and a read path must never invoke an
        // IReminderRecipientResolver (resolvers run at dispatch and may reach owner domain code) — the same
        // rule GetMyUpcomingRemindersEndpoint follows.
        var reminders = await dbContext.Set<ReminderRecipient>()
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.ReminderDefinition.RecipientMode == RecipientMode.ExplicitUsers)
            .OrderByDescending(r => r.ReminderDefinition.NextOccurrenceAt)
            .ThenBy(r => r.ReminderDefinitionId)
            .Select(r => new ExportReminder(
                r.ReminderDefinitionId,
                r.ReminderDefinition.OwnerModule,
                r.ReminderDefinition.SubjectType,
                r.ReminderDefinition.SubjectId,
                r.ReminderDefinition.Kind,
                r.ReminderDefinition.TemplateKey,
                r.ReminderDefinition.ScheduleType,
                r.ReminderDefinition.Status,
                r.ReminderDefinition.NextOccurrenceAt,
                r.CreatedTimestamp))
            .ToListAsync(ct);

        var actions = await dbContext.Set<ReminderOccurrenceAction>()
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.ActedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new ExportReminderAction(
                a.Id,
                a.ReminderDefinitionId,
                a.OccurrenceAt,
                a.ActionType,
                a.SnoozeUntil,
                a.ActedAt,
                a.ReversesActionId))
            .ToListAsync(ct);

        var preferences = await dbContext.Set<ReminderKindPreference>()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.OwnerModule)
            .ThenBy(p => p.Kind)
            .Select(p => new ExportReminderKindPreference(
                p.Id,
                p.OwnerModule,
                p.Kind,
                p.Enabled,
                p.ChannelHint))
            .ToListAsync(ct);

        if (reminders.Count == 0 && actions.Count == 0 && preferences.Count == 0)
            return null;

        return new RemindersPersonalData(reminders, actions, preferences);
    }

    private sealed record RemindersPersonalData(
        IReadOnlyList<ExportReminder> Reminders,
        IReadOnlyList<ExportReminderAction> OccurrenceActions,
        IReadOnlyList<ExportReminderKindPreference> KindPreferences);

    /// <summary>A reminder the subject is an explicit recipient of. No payload, no co-recipients.</summary>
    private sealed record ExportReminder(
        long ReminderDefinitionId,
        string OwnerModule,
        string SubjectType,
        string SubjectId,
        string Kind,
        string TemplateKey,
        ReminderScheduleType ScheduleType,
        ReminderStatus Status,
        DateTimeOffset? NextOccurrenceAt,
        DateTime RecipientSince);

    /// <summary>The subject's own snooze / dismiss / reversal history.</summary>
    private sealed record ExportReminderAction(
        long Id,
        long ReminderDefinitionId,
        DateTimeOffset OccurrenceAt,
        ReminderActionType ActionType,
        DateTimeOffset? SnoozeUntil,
        DateTimeOffset ActedAt,
        long? ReversesActionId);

    /// <summary>The subject's per-kind opt-outs (absence means enabled).</summary>
    private sealed record ExportReminderKindPreference(
        long Id,
        string OwnerModule,
        string Kind,
        bool Enabled,
        NotificationChannel? ChannelHint);
}