using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

public class GetByIdTodoListItemEndpoint(
    DbContext dbContext,
    ITodoListItemPlanSource planSource,
    IUserTimeZoneResolver timeZones)
    : BaseGetByIdEndpoint<TodoListItem, TodoListItemResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TodoListItemResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>The planner's next slot for the item — the same overlay the list read applies.</summary>
    protected override async Task<TodoListItemResponse> PostProcessAsync(TodoListItemResponse entity, CancellationToken ct) =>
        (await TodoListItemPlannedSlotFiller.FillAsync([entity], _dbContext, planSource, timeZones, User.GetId(), ct))[0];
}
