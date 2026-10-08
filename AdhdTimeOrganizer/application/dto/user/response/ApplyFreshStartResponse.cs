namespace AdhdTimeOrganizer.application.dto.response.user;

/// <summary><c>POST /welcome-back/fresh-start</c> (FO5): what changed, and the token that takes it back.</summary>
public record ApplyFreshStartResponse
{
    /// <summary>
    /// Echo to <c>POST /welcome-back/fresh-start/undo</c> within <c>FreshStartUndo.Lifetime</c> to put everything
    /// back and reopen the card. Opaque. Held in the server's memory, so a restart forgets it.
    /// </summary>
    public required string UndoToken { get; init; }

    /// <summary>To-do items whose due date changed (cleared, or spread).</summary>
    public required IReadOnlyList<long> RescheduledTodoIds { get; init; }

    /// <summary>Planner tasks skipped with the reason <c>away</c>.</summary>
    public required IReadOnlyList<long> ClosedTaskIds { get; init; }

    /// <summary>Planner tasks carried to today.</summary>
    public required IReadOnlyList<long> CarriedTaskIds { get; init; }

    public required int NotificationsMarkedRead { get; init; }
}

/// <summary><c>POST /welcome-back/fresh-start/undo</c>: what was put back.</summary>
/// <remarks>
/// A row changed again after the fresh start (an item ticked off, a task moved) is left as it now is and is missing
/// from these lists: undo never overwrites the user's own later edit.
/// </remarks>
public record UndoFreshStartResponse
{
    public required IReadOnlyList<long> RestoredTodoIds { get; init; }

    public required IReadOnlyList<long> RestoredTaskIds { get; init; }

    public required int NotificationsRestored { get; init; }
}
