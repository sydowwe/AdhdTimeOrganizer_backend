using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.query;

/// <summary>
/// Single registered reminder by id, with its explicit recipients and one-shot lead offsets (via
/// <see cref="ReminderDefinitionDto.Projection"/>). open to any signed-in user (infra inspector).
/// </summary>
public class GetReminderByIdEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<ReminderDefinition, ReminderDefinitionDto>(dbContext)
{
    protected override Task<bool> AuthorizeAsync(ReminderDefinitionDto entity, CancellationToken ct) => Task.FromResult(true);
}