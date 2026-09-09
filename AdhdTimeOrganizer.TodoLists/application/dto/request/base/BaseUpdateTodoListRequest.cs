using AdhdTimeOrganizer.Core.application.dto.request.extendable;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

/// <summary>
/// The fields both PUT surfaces share. <c>DisplayOrder</c> and <c>DoneCount</c> are deliberately absent:
/// they are server-owned on both, so the request shape simply cannot carry them.
/// </summary>
/// <remarks>
/// <c>DisplayOrder</c> belongs to the reorder endpoints (<c>BaseChangeDisplayOrderTodoListEndpoint</c>
/// and the create path's <c>GetNextDisplayOrder</c>), which is the whole point of having them — a PUT
/// that could place an item at an arbitrary order could also tie it with a sibling, the exact collision
/// the reorder endpoint exists to avoid.
/// <para>
/// <c>DoneCount</c> is derived from <c>TotalCount</c> and <c>IsDone</c> in each endpoint's
/// <c>AfterMapping</c>, never taken from the body. Accepting it let a PUT claim any count the validator's
/// <c>DoneCount &lt;= TotalCount</c> rule permitted, independently of how many steps were actually ticked —
/// so "done" had three representations that no single caller reconciled.
/// </para>
/// </remarks>
public record BaseUpdateTodoListRequest : WithIsDoneRequest
{
    public int? TotalCount { get; set; }
    public string? Note { get; set; }
    public IntTime? SuggestedTime { get; set; }
    public List<StepRequest>? Steps { get; init; }
}