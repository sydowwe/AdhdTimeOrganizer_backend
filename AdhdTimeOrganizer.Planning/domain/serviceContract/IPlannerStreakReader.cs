using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// Reads the day-plan completion streak for one user. The single entry point for it — no endpoint should
/// assemble the tallies itself, because the qualifying-task predicate is half of the rule set and duplicating
/// it is how the client and the server end up disagreeing about the same number.
/// </summary>
public interface IPlannerStreakReader
{
    /// <summary>
    /// Compute the streak from the user's planner-task rows. Always current — nothing is cached and nothing is
    /// stored, so this reflects a status patch made a millisecond ago, including one that <i>un</i>-ticked a
    /// task or edited a day weeks back.
    /// </summary>
    Task<PlannerStreakResponse> GetForUserAsync(long userId, CancellationToken ct = default);
}
