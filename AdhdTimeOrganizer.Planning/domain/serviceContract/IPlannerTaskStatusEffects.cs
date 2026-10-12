using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// What follows a planner task's status change once it has been saved: its reminder is synced (finishing or skipping a
/// block retires it, re-opening one brings it back) and, for a task on the user's today, the done-changed event goes out
/// to the slices that mirror it (the to-do item it came from, routine streaks). One copy, shared by
/// <c>PatchPlannerTaskStatusEndpoint</c> and the push action buttons, so a block finished from the lock screen has
/// every effect one finished in the app has.
/// </summary>
public interface IPlannerTaskStatusEffects
{
    /// <param name="task">The saved task, with <see cref="PlannerTask.Calendar"/> loaded.</param>
    Task AfterStatusSavedAsync(PlannerTask task, CancellationToken ct = default);
}
