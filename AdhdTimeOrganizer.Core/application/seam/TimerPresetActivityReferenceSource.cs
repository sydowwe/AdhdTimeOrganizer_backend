using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Core's own contribution to <see cref="IActivityReferenceSource"/>: the three nullable activity
/// columns on the two timer preset entities.
/// </summary>
/// <remarks>
/// <para>
/// Core implements a seam it also consumes, which looks redundant and is not: the alternative is a
/// special case inside <c>ActivityReferenceService</c> for "the entities Core happens to own", and
/// then presets are counted by different code than everything else and drift the first time one of
/// them changes. Going through the same interface is what makes the seam registry the whole answer to
/// "what counts as a reference".
/// </para>
/// <para>
/// Presets are the one family where "cheap to recreate" is a real argument for leaving them out of
/// <c>usageCount</c>. They are counted anyway, and the reason is stronger than it looks: all three
/// columns are nullable but their FKs are <c>DeleteBehavior.Cascade</c>, not <c>SetNull</c>, so
/// deleting the activity deletes the <em>whole preset row</em> rather than blanking one column. A
/// <c>canDelete: true</c> there would silently take a user's pomodoro configuration with it — exactly
/// the broken promise the field exists to prevent.
/// </para>
/// <para>
/// <c>PomodoroTimerPreset</c> yields once per column, so a preset whose focus <em>and</em> rest
/// activity are both the merge target counts as two references — see the duplicate rule on the
/// interface.
/// </para>
/// </remarks>
public sealed class TimerPresetActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.TimerPreset;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<TimerPreset>()
            .Where(p => p.ActivityId != null)
            .Select(p => p.ActivityId!.Value)
            .Concat(db.Set<PomodoroTimerPreset>()
                .Where(p => p.FocusActivityId != null)
                .Select(p => p.FocusActivityId!.Value))
            .Concat(db.Set<PomodoroTimerPreset>()
                .Where(p => p.RestActivityId != null)
                .Select(p => p.RestActivityId!.Value));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var repointed = 0;

        var presets = await db.Set<TimerPreset>()
            .Where(p => p.ActivityId != null && mergedIds.Contains(p.ActivityId.Value))
            .ToListAsync(ct);
        foreach (var preset in presets)
        {
            preset.ActivityId = survivorId;
            repointed++;
        }

        // Both columns are checked on every loaded row rather than filtering twice, so a preset holding
        // two merged activities is loaded once and counts twice — the same count the union produces.
        var pomodoros = await db.Set<PomodoroTimerPreset>()
            .Where(p => (p.FocusActivityId != null && mergedIds.Contains(p.FocusActivityId.Value))
                        || (p.RestActivityId != null && mergedIds.Contains(p.RestActivityId.Value)))
            .ToListAsync(ct);
        foreach (var pomodoro in pomodoros)
        {
            if (pomodoro.FocusActivityId is { } focus && mergedIds.Contains(focus))
            {
                pomodoro.FocusActivityId = survivorId;
                repointed++;
            }

            if (pomodoro.RestActivityId is { } rest && mergedIds.Contains(rest))
            {
                pomodoro.RestActivityId = survivorId;
                repointed++;
            }
        }

        return repointed;
    }
}
