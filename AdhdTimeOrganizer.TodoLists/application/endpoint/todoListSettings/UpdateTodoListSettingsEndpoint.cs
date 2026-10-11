using System.Linq.Expressions;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListSettings;

/// <summary>
/// Upserts the caller's to-do settings. A changed lead re-registers every open timed item, because each registered
/// reminder carries the offset it was built with — without the re-sync the new setting would only reach items
/// edited after it.
/// </summary>
public class UpdateTodoListSettingsEndpoint(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : BaseUserSettingsUpsertEndpoint<UserTodoListSettings, UserTodoListSettingsRequest>(dbContext)
{
    protected override string Route => "/todo-list/settings";

    public override void Configure()
    {
        base.Configure();
        Validator<UserTodoListSettingsValidator>();
        Summary(s =>
        {
            s.Summary = "Update to-do settings for the current user";
            s.Response(204, "Success");
            s.Response(400, "Bad request");
        });
    }

    protected override Expression<Func<UserTodoListSettings, bool>> MatchRow(long userId, UserTodoListSettingsRequest req) =>
        s => s.UserId == userId;

    // UserId explicitly: this is the only insert path, and it must not depend on the ambient user.
    protected override UserTodoListSettings NewRow(long userId, UserTodoListSettingsRequest req) => new()
    {
        UserId = userId,
        DueReminderMinutesBefore = req.DueReminderMinutesBefore
    };

    protected override void Apply(UserTodoListSettings row, UserTodoListSettingsRequest req) =>
        row.DueReminderMinutesBefore = req.DueReminderMinutesBefore;

    protected override Task AfterUpsertAsync(long userId, UserTodoListSettingsRequest req, CancellationToken ct = default) =>
        dueReminders.SyncAllForUserAsync(userId, ct);
}
