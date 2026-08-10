using AdhdTimeOrganizer.Planning.application.dto.response.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.reminder.query;

public class GetByIdReminderEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<Reminder, ReminderResponse>(dbContext)
{
    /// <summary>
    /// The read already went through <c>AppDbContext</c>'s user query filter, so another user's reminder never
    /// reaches this hook — a foreign id is indistinguishable from a missing one, which is the behaviour we
    /// want anyway (an id's mere existence is not the caller's business).
    /// </summary>
    protected override Task<bool> AuthorizeAsync(ReminderResponse entity, CancellationToken ct) => Task.FromResult(true);
}