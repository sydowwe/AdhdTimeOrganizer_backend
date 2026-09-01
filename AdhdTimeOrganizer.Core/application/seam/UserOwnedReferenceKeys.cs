namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// The known <see cref="IUserOwnedReferenceVerifier.Key"/> values.
/// </summary>
/// <remarks>
/// Lives in Core so the owning slice and the checking slice agree on the string without either
/// referencing the other. A key with no verifier makes every write carrying that id a 404 —
/// <c>IReferencedIdOwnershipService</c> fails closed — so a typo here is loud rather than silent, but
/// it is loud in production, not at build time. Both sides read the constant.
/// </remarks>
public static class UserOwnedReferenceKeys
{
    /// <summary>TodoLists' <c>TodoListItem</c>.</summary>
    public const string TodoListItem = "todoListItem";

    /// <summary>Routines' <c>RoutineTodoList</c>.</summary>
    public const string RoutineTodoList = "routineTodoList";
}
