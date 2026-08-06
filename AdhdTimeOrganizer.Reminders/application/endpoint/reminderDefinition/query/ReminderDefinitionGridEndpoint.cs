using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.query;

/// <summary>
/// Registered-reminders grid (POST /reminder-definition/filtered-table). Open to any signed-in user
/// (User/Admin/Root) via the base default. The base <c>ApplyUserScoping</c> is still a no-op, so this
/// returns EVERY user's reminder definitions — fine for a single-user deployment, but override
/// <c>ApplyUserScoping</c> (scope by <c>Recipients</c>) before this app is multi-tenant. The
/// per-recipient dashboard is phase 05.
/// </summary>
public class ReminderDefinitionGridEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ReminderDefinition, ReminderDefinitionDto, ReminderDefinitionFilterRequest>(dbContext)
{
    protected override IQueryable<ReminderDefinition> ApplyCustomFiltering(IQueryable<ReminderDefinition> query, ReminderDefinitionFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.OwnerModule))
            query = query.Where(x => x.OwnerModule == filter.OwnerModule);

        if (!string.IsNullOrWhiteSpace(filter.SubjectType))
            query = query.Where(x => x.SubjectType == filter.SubjectType);

        if (!string.IsNullOrWhiteSpace(filter.Kind))
            query = query.Where(x => x.Kind == filter.Kind);

        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);

        if (filter.ScheduleType.HasValue)
            query = query.Where(x => x.ScheduleType == filter.ScheduleType.Value);

        if (filter.NextOccurrenceFrom.HasValue)
            query = query.Where(x => x.NextOccurrenceAt >= filter.NextOccurrenceFrom.Value);

        if (filter.NextOccurrenceTo.HasValue)
            query = query.Where(x => x.NextOccurrenceAt <= filter.NextOccurrenceTo.Value);

        return query;
    }
}