using System.Collections.Concurrent;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The authoritative per-<c>JobKey</c> concurrency enforcement for <c>DisallowConcurrent</c> jobs. The
/// deployment model is a single-node modular monolith (one process), so an in-process gate is sufficient
/// <i>by design</i> — not a stopgap. A second fire of a job whose gate is already held is skipped (a
/// non-blocking try-acquire), never queued behind the in-flight run.
/// <para>
/// Singleton (one set of gates per process). If the deployment ever became multi-node — not anticipated —
/// this would need to move to a DB-backed cluster-wide lock alongside Quartz clustering + a persistent
/// store. Until then the gate + the append-only run-log dedup cover the single-node double-fire cases.
/// </para>
/// </summary>
public interface IJobConcurrencyGate
{
    /// <summary>Non-blocking try-acquire for a job key. <c>true</c> if acquired; <c>false</c> if already held.</summary>
    bool TryAcquire(string jobKey);

    /// <summary>Release a gate acquired by <see cref="TryAcquire"/>. No-op if never acquired.</summary>
    void Release(string jobKey);
}

public sealed class JobConcurrencyGate : IJobConcurrencyGate, ISingletonService, IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public bool TryAcquire(string jobKey)
    {
        return _gates.GetOrAdd(jobKey, _ => new SemaphoreSlim(1, 1)).Wait(0);
    }

    public void Release(string jobKey)
    {
        if (_gates.TryGetValue(jobKey, out var gate))
            gate.Release();
    }

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Dispose();
    }
}