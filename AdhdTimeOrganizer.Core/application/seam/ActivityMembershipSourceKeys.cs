namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// The known <see cref="IActivityMembershipSource.Key"/> values.
/// </summary>
/// <remarks>
/// Lives in Core so the producing slice and the consuming slice agree on the string without either
/// referencing the other — that is the whole point of the seam. A typo here does not fail the build,
/// it silently makes the filter a no-op, which is why both sides read the constant rather than
/// re-typing the literal.
/// </remarks>
public static class ActivityMembershipSourceKeys
{
    public const string TodoList = "todoList";
    public const string RoutineTodoList = "routineTodoList";
}
