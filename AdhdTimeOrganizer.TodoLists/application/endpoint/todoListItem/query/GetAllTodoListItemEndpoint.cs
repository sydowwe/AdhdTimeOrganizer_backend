using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

public class GetAllTodoListItemRequest
{
    /// <summary>
    /// Required — see <see cref="GetAllTodoListItemValidator"/>. This route reads one list, not the caller's
    /// whole to-do universe.
    /// </summary>
    public long TodoListId { get; set; }
}

public class GetAllTodoListItemEndpoint(
    DbContext dbContext,
    ITodoListItemPlanSource planSource,
    IUserTimeZoneResolver timeZones)
    : Endpoint<GetAllTodoListItemRequest, List<TodoListItemResponse>>
{
    public override void Configure()
    {
        Get("/todo-list-item");
        Validator<GetAllTodoListItemValidator>();

        Summary(s =>
        {
            s.Summary = "Get one to-do list's items";
            s.Description = "Retrieves the caller's items on the given to-do list, in DisplayOrder. TodoListId is required.";
            s.Response<List<TodoListItemResponse>>(200, "Success");
            s.Response(400, "TodoListId missing or not positive");
        });
    }

    public override async Task HandleAsync(GetAllTodoListItemRequest req, CancellationToken ct)
    {
        var loggedUserId = User.GetId();

        var query = dbContext.Set<TodoListItem>()
            .FilteredByUser(loggedUserId)
            .Where(tdl => tdl.TodoListId == req.TodoListId)
            .OrderBy(td => td.DisplayOrder);

        var items = await TodoListItemResponse.Projection(query).ToListAsync(ct);
        items = await TodoListItemPlannedSlotFiller.FillAsync(items, dbContext, planSource, timeZones, loggedUserId, ct);
        await Send.OkAsync(items, ct);
    }
}
