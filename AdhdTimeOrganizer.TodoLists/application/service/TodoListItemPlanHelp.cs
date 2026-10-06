using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.TodoLists.application.service;

/// <summary>
/// FO3: decides whether a to-do item is stuck in the planner, and if so which kind of help to offer first. The
/// answer is a wire token on <c>TodoListItemResponse.PlanHelp</c>. The counts it is computed from never leave this
/// class: the feature offers a smaller first step, and showing "pushed four times" would make it a score instead.
/// <para>
/// <b>Stuck</b> means at least <see cref="StuckThreshold"/> failed attempts (skipped, carried over, or missed) and
/// none completed. A completed attempt clears it for good. The item has happened at least once, so whatever is
/// still open on it is not the "can't start" problem this answers. Tracker-completed blocks count as completed,
/// which is the forgiving direction.
/// </para>
/// <para>
/// <b>Which help</b> follows the dominant way the attempts failed:
/// <list type="bullet">
/// <item>"Not relevant any more" → <see cref="Reconsider"/>: the user has already said so; offer to let it go.</item>
/// <item>"Ran out of time" → <see cref="ShorterBlock"/>: a 10-minute start fits where the full block did not.</item>
/// <item>"No energy", a carry-over, a missed block or a free-text skip → <see cref="SmallerStep"/>: the item is too
/// big to begin, so the offer is to split it.</item>
/// </list>
/// Ties go to <see cref="SmallerStep"/>, then <see cref="ShorterBlock"/>. <see cref="Reconsider"/> suggests
/// dropping the task, so it is offered only when the user's own answers clearly point there.
/// </para>
/// </summary>
public static class TodoListItemPlanHelp
{
    public const int StuckThreshold = 3;

    public const string SmallerStep = "smallerStep";
    public const string ShorterBlock = "shorterBlock";
    public const string Reconsider = "reconsider";

    /// <summary>The help token for one item, or null when it is not stuck.</summary>
    public static string? For(PlanHistory? history)
    {
        if (history is null || history.Completed > 0 || history.FailedAttempts < StuckThreshold)
            return null;

        var smallerStep = history.SkippedNoEnergy + history.CarriedOver + history.Missed + history.SkippedOther;
        var shorterBlock = history.SkippedNoTime;
        var reconsider = history.SkippedNotRelevant;

        if (smallerStep >= shorterBlock && smallerStep >= reconsider)
            return SmallerStep;
        return shorterBlock >= reconsider ? ShorterBlock : Reconsider;
    }
}
