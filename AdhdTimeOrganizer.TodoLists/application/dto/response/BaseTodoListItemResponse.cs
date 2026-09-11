using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record BaseTodoListResponse : WithIsDoneResponse
{
    public long DisplayOrder { get; init; }

    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// The user's own estimate for the item, mirroring <c>BaseTodoListItem.SuggestedTime</c>.
    /// It lives on the shared base for the same reason the column does: both kinds of item carry one.
    /// <para>
    /// It was declared only on the routine response for a while, so <c>GET /api/todo-list-item</c>
    /// returned no estimate at all and the SPA's estimate-vs-actual chip and header calibration ratio
    /// had nothing to render — even though the rows were there and the e2e fixture was built to feed
    /// them. Every projection over a to-do item must set it; the guard is
    /// <c>TestSeedEndpointTests.Seed_LeavesTheEstimateVisibleThroughTheProjection</c>.
    /// </para>
    /// </summary>
    public IntTime? SuggestedTime { get; init; }

    public List<TodoListStepResponse> Steps { get; init; } = [];
}