using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Core.application.service;

/// <summary>
/// Fans an activity removal out to every <see cref="IActivityRemovalParticipant"/> and folds their after-commit
/// work into one callback. Each participant's callback is isolated: the removal has committed by the time any of
/// them runs, so one participant's failure is logged and must not cost the others theirs.
/// </summary>
public sealed class ActivityRemovalService(
    IEnumerable<IActivityRemovalParticipant> participants,
    ILogger<ActivityRemovalService> logger) : IActivityRemovalService, IScopedService
{
    public async Task<Func<CancellationToken, Task>> BeforeRemovalAsync(
        DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct)
    {
        var afterCommit = new List<(string Key, Func<CancellationToken, Task> Run)>();
        foreach (var participant in participants)
        {
            if (await participant.BeforeRemovalAsync(db, activityIds, ct) is { } run)
                afterCommit.Add((participant.Key, run));
        }

        return async token =>
        {
            foreach (var (key, run) in afterCommit)
            {
                try
                {
                    await run(token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex,
                        "Activity removal participant {ParticipantKey} failed after the removal of activities {ActivityIds} committed",
                        key, activityIds);
                }
            }
        };
    }
}
