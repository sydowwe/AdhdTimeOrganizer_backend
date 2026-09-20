using FastEndpoints;
using FluentValidation.Results;
using Npgsql;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.extensions;

/// <summary>
/// The save every ingest route ends on, with a unique-index collision answered as a 409 instead of a 500
/// (CQ-11, <c>review/tracking/02-findings.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The CRUD bases already do this — <c>BaseCreateEndpoint</c> and <c>BaseUpdateEndpoint</c> route every
/// exception through <c>DbUtils.HandleException</c>, which maps <c>23505</c> to
/// <see cref="ResultErrorType.DbUniqueViolationError"/> and so to a 409. The three ingest endpoints are
/// plain <c>Endpoint&lt;&gt;</c>s with no base and no try/catch, so a collision there reached the global
/// handler as a 500. That matters more here than on a CRUD route: the clients are a desktop agent, a
/// browser extension and a phone, none of which has a person watching it, and all of which retry. A 500 on
/// a batch they will re-post unchanged is a loop; a 409 on a batch whose rows the endpoint now skips on the
/// next attempt is not.
/// </para>
/// <para>
/// Only <c>23505</c> is handled. Anything else still propagates untouched — a foreign-key or check
/// violation on an ingest path is a bug in this slice, not something a client can resolve by retrying.
/// </para>
/// </remarks>
public static class IngestBatchSaveExtensions
{
    private const string ConflictMessage =
        "Part of this batch was recorded by a concurrent request. Retry it -- rows already stored are skipped.";

    /// <returns><c>true</c> when the batch committed; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> TrySaveIngestBatchAsync(this IEndpoint endpoint, DbContext dbContext, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            endpoint.ValidationFailures.Add(new ValidationFailure(string.Empty, ConflictMessage));
            await endpoint.HttpContext.Response.SendErrorsAsync(
                endpoint.ValidationFailures,
                // Not a literal 409: the ingest routes answer a unique violation with the same status the
                // CRUD bases give it, and that mapping lives in one place. ErrorStatus also stamps the
                // type onto the body's errorType, so the client can word it as a duplicate (B18).
                endpoint.ErrorStatus(ResultErrorType.DbUniqueViolationError),
                cancellation: ct);
            return false;
        }
    }
}
