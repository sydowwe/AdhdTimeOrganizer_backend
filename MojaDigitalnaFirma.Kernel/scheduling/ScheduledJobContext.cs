using Sydowwe.Framework.domain.helper;

namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// The execution context handed to an <see cref="IScheduledJobHandler"/> on each invocation. Carries
/// the timing, identity, opaque payload and provenance the Scheduler captured for this fire. The
/// <see cref="System.Threading.CancellationToken"/> is a separate parameter on
/// <see cref="IScheduledJobHandler.ExecuteAsync"/>, not a field here.
/// </summary>
public sealed class ScheduledJobContext
{
    /// <summary>When this fire was scheduled for. Null for a manual/replay fire that is off-schedule.</summary>
    public required DateTime? ScheduledFireTimeUtc { get; init; }

    /// <summary>When the handler actually started.</summary>
    public required DateTime ActualFireTimeUtc { get; init; }

    /// <summary>The job's key.</summary>
    public required string JobKey { get; init; }

    /// <summary>Raw JSON payload captured for this run (null when the job has no payload).</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Correlation id for tracing this run across logs/audit.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>How this run was initiated.</summary>
    public required TriggerSource TriggerSource { get; init; }

    /// <summary>
    /// Typed accessor over <see cref="PayloadJson"/>. Returns default when there is no payload. The
    /// deserialised value should never carry PII (name/address/free text) — see
    /// <see cref="RecurringJobRegistration.Payload"/>; pass ids and re-fetch domain data in the handler.
    /// </summary>
    public T? GetPayload<T>() => string.IsNullOrEmpty(PayloadJson) ? default : JsonHelper.Deserialize<T>(PayloadJson);
}