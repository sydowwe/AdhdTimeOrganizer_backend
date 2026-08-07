using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.Scheduler.domain.serviceContract;

/// <summary>
/// Re-runs a past <c>ScheduledJobRun</c> <b>through the single phase-03 dispatcher</b> — never a forked
/// execution path. It loads the original run, validates it can be replayed (the run exists and its
/// <c>HandlerKeySnapshot</c> still resolves to a registered handler), then fires the durable dispatcher job
/// off-schedule with a <c>TriggerSource = Replay</c> data map carrying the original run's <i>snapshotted</i>
/// payload and id. The dispatcher builds the context, applies the per-key concurrency gate and writes the
/// new, linked run row — this service builds no context and writes no run row.
/// <para>
/// Replay is an operator/dashboard concern, so it is deliberately <b>not</b> on the <c>Sydowwe.Framework.Contracts</c>
/// <c>IScheduler</c> contract (owners register/control; they never replay). <b>Replaying a non-idempotent
/// job repeats its side effects</b> — effect-idempotency is the handler's responsibility.
/// </para>
/// </summary>
public interface IScheduledRunReplayer
{
    Task<Result> ReplayRunAsync(long runId, CancellationToken ct);
}