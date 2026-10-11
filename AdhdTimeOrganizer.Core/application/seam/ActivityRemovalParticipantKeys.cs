namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// The <see cref="IActivityRemovalParticipant.Key"/> of every participant the solution registers.
/// <c>SeamWiringTests.ActivityRemovalParticipants_CoverEveryKey_Exactly</c> holds the container to this list.
/// </summary>
public static class ActivityRemovalParticipantKeys
{
    /// <summary>TodoLists: the due reminders of the to-dos on the activity.</summary>
    public const string TodoList = "todoList";
}
