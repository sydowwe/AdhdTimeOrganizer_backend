using System.Linq.Expressions;
using FastEndpoints;
using FluentValidation.Results;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.extensions;

/// <summary>
/// The pre-check that turns a duplicate mapping pattern into a 409 <em>naming the pattern</em> (CQ-11).
/// </summary>
/// <remarks>
/// <para>
/// The per-user pattern index is the most user-reachable constraint in the module — "map <c>code.exe</c>
/// to Programming" twice is an ordinary mistake, and <c>NULLS NOT DISTINCT</c> makes two rules that both
/// leave <c>ProcessName</c> null collide as well, which is the half nobody expects. The CRUD bases
/// already answer the collision with a 409 (<c>DbUtils.HandleException</c> maps <c>23505</c>), so the
/// status was never wrong; what the user got was "Unique constraint violation during Create" rather than
/// which of their rules they had just written twice. This is the other half of CQ-11 — the named fix its
/// text asks for — and it is the reason the check is here rather than left entirely to the index.
/// </para>
/// <para>
/// It does not replace the index. Two requests in flight still race past a pre-check, and the base's
/// handler catches that with the same status; this only makes the ordinary case explicable.
/// </para>
/// </remarks>
public static class MappingPatternUniquenessExtensions
{
    /// <summary>
    /// Answers 409 when the caller already has a mapping with this pattern tuple.
    /// </summary>
    /// <param name="samePattern">
    /// The pattern-tuple equality, exactly the columns in the entity's unique index. Written with plain
    /// <c>==</c> against the request's (possibly null) values on purpose: EF Core compiles a null-valued
    /// parameter into <c>IS NULL</c>, which is the <c>NULLS NOT DISTINCT</c> semantic the index has.
    /// </param>
    /// <param name="excludedId">The row being updated, so a rule does not collide with itself.</param>
    /// <param name="patternDescription">
    /// What the user typed, echoed back — see <see cref="DescribePattern"/>. Not logged; it goes to the
    /// caller who just sent it.
    /// </param>
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureMappingPatternIsUniqueAsync<TEntity>(
        this IEndpoint endpoint,
        DbContext db,
        Expression<Func<TEntity, bool>> samePattern,
        long? excludedId,
        string patternDescription,
        CancellationToken ct)
        where TEntity : class, IEntityWithUser
    {
        var userId = endpoint.HttpContext.User.GetId();

        // Scoped by hand as well as by the global filter: this is a module read, and the index it mirrors
        // is per user — an unscoped one would report another account's rule as the caller's duplicate.
        var query = db.Set<TEntity>().AsNoTracking()
            .Where(e => e.UserId == userId)
            .Where(samePattern);

        if (excludedId is { } id)
            query = query.Where(e => e.Id != id);

        if (!await query.AnyAsync(ct))
            return true;

        endpoint.ValidationFailures.Add(new ValidationFailure(
            "Patterns",
            $"You already have a mapping for {patternDescription}."));

        await endpoint.HttpContext.Response.SendErrorsAsync(
            endpoint.ValidationFailures,
            // Not a literal 409: a duplicate is the same unique violation the CRUD bases answer when the
            // index catches one, and that mapping lives in one place.
            EndpointHelper.ToStatusCode(ResultErrorType.DbUniqueViolationError),
            cancellation: ct);
        return false;
    }

    /// <summary>
    /// Renders the pattern columns the request actually set — "ProcessName 'code.exe'". The validators
    /// guarantee at least one, so this is never empty.
    /// </summary>
    public static string DescribePattern(params (string Field, string? Value)[] parts) =>
        string.Join(", ", parts.Where(p => p.Value != null).Select(p => $"{p.Field} '{p.Value}'"));
}
