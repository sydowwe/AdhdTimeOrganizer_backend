using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

/// <summary>
/// The items this user completed on a given day, with the time recorded against each.
/// </summary>
/// <remarks>
/// Not expressible on the existing endpoints, which is why it is its own route: the item endpoints
/// carry <c>IsDone</c> but nothing dated, so a task finished an hour ago and one finished last March
/// are indistinguishable there, and History's aggregates key time to an activity rather than to an
/// item.
/// <para>
/// Logged time is read through <see cref="ITodoListItemLoggedTimeSource"/> — it lives in
/// <c>ActivityHistory</c>, which this slice cannot see. The seam resolves as a single service, so a
/// missing registration throws here at activation rather than reporting every day as empty.
/// </para>
/// </remarks>
public class GetDailyRecapTodoListItemEndpoint(DbContext db, ITodoListItemLoggedTimeSource loggedTime)
    : Endpoint<TodoListItemDailyRecapRequest, TodoListItemDailyRecapResponse>
{
    public override void Configure()
    {
        Get("/todo-list-item/daily-recap");
        Roles(this.GetUserRole());
        Validator<TodoListItemDailyRecapValidator>();

        Summary(s =>
        {
            s.Summary = "Items completed on a given day, with time recorded against each";
            s.Response<TodoListItemDailyRecapResponse>(200, "The recap; Items is empty on a zero day");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TodoListItemDailyRecapRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // Half-open UTC range, matching how every History dashboard bounds a day — a closed range on
        // TimeOnly.MaxValue drops whatever lands in the final tick.
        var from = req.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);

        // Filtered on UserId explicitly rather than leaning on AppDbContext's global filter: this is
        // the row set the response is built from, and it must follow the id that was authenticated.
        var completed = await db.Set<TodoListItem>()
            .AsNoTracking()
            .Where(i => i.UserId == userId
                        && i.CompletedTimestamp != null
                        && i.CompletedTimestamp >= from && i.CompletedTimestamp < to)
            .OrderBy(i => i.CompletedTimestamp)
            .Select(i => new { i.Id, i.Activity.Name })
            .ToListAsync(ct);

        if (completed.Count == 0)
        {
            await Send.ResponseAsync(
                new TodoListItemDailyRecapResponse { Items = [], TotalTimeLoggedSeconds = 0 },
                cancellation: ct);
            return;
        }

        var secondsByItem = await loggedTime.LoggedSecondsOnDayAsync(
            db, userId, completed.Select(c => c.Id).ToList(), req.Date, ct);

        var items = completed
            .Select(c => new DailyRecapItem
            {
                Name = c.Name,
                // Absent means no recording was saved for that item — the prompt is declined often
                // enough that a completed task with no time against it is an ordinary case, not a gap.
                TimeLoggedSeconds = secondsByItem.GetValueOrDefault(c.Id)
            })
            .ToList();

        await Send.ResponseAsync(
            new TodoListItemDailyRecapResponse
            {
                Items = items,
                TotalTimeLoggedSeconds = items.Sum(i => i.TimeLoggedSeconds)
            },
            cancellation: ct);
    }
}
