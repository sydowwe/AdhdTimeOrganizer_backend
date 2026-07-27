using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Shared, SQL-translatable filter builders for the dashboard reads — used by both the paged JSON endpoints and
/// their <c>Export*</c> variants so the file can never drift from the API. No paging/sorting/projection here.
/// </summary>
internal static class ReminderDashboardQueries
{
    /// <summary>Base upcoming set: definitions with a pending occurrence (<c>NextOccurrenceAt != null</c>, i.e. active).</summary>
    public static IQueryable<ReminderDefinition> Upcoming(DbContext db)
    {
        return db.Set<ReminderDefinition>().AsNoTracking().Where(x => x.NextOccurrenceAt != null);
    }

    public static IQueryable<ReminderDefinition> ApplyUpcomingFilter(IQueryable<ReminderDefinition> query, UpcomingReminderFilterRequest f)
    {
        if (!string.IsNullOrWhiteSpace(f.OwnerModule))
            query = query.Where(x => x.OwnerModule == f.OwnerModule);
        if (!string.IsNullOrWhiteSpace(f.SubjectType))
            query = query.Where(x => x.SubjectType == f.SubjectType);
        if (!string.IsNullOrWhiteSpace(f.Kind))
            query = query.Where(x => x.Kind == f.Kind);
        if (f.RecipientUserId.HasValue)
            query = query.Where(x => x.Recipients.Any(r => r.UserId == f.RecipientUserId.Value));
        if (f.Status.HasValue)
            query = query.Where(x => x.Status == f.Status.Value);
        if (f.ScheduleType.HasValue)
            query = query.Where(x => x.ScheduleType == f.ScheduleType.Value);
        if (f.NextOccurrenceFrom.HasValue)
            query = query.Where(x => x.NextOccurrenceAt >= f.NextOccurrenceFrom.Value);
        if (f.NextOccurrenceTo.HasValue)
            query = query.Where(x => x.NextOccurrenceAt <= f.NextOccurrenceTo.Value);
        return query;
    }

    public static IQueryable<ReminderDispatch> ApplyDispatchHistoryFilter(IQueryable<ReminderDispatch> query, ReminderDispatchHistoryFilterRequest f)
    {
        if (f.ReminderDefinitionId.HasValue)
            query = query.Where(x => x.ReminderDefinitionId == f.ReminderDefinitionId.Value);
        if (!string.IsNullOrWhiteSpace(f.OwnerModule))
            query = query.Where(x => x.ReminderDefinition.OwnerModule == f.OwnerModule);
        if (f.RecipientUserId.HasValue)
            query = query.Where(x => x.ReminderDefinition.Recipients.Any(r => r.UserId == f.RecipientUserId.Value));
        if (f.Outcome.HasValue)
            query = query.Where(x => x.Outcome == f.Outcome.Value);
        if (f.DispatchedFrom.HasValue)
            query = query.Where(x => x.DispatchedAt >= f.DispatchedFrom.Value);
        if (f.DispatchedTo.HasValue)
            query = query.Where(x => x.DispatchedAt <= f.DispatchedTo.Value);
        return query;
    }
}