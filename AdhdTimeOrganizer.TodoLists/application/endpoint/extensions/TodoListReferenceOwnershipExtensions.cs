using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.extensions;

/// <summary>
/// The 404-on-a-foreign-id half of <see cref="CallerVisibleReferenceExtensions"/> — the check and the
/// answer in one call, so every write path in this slice rejects a caller-supplied FK id the same way.
/// </summary>
/// <remarks>
/// <para>
/// Four endpoints need this (create, update, move, change-priority) and between them they carry six
/// ids, so the alternative was six copies of "if not visible, add an error, send 404". The 404 is not
/// incidental: a 403 would confirm the row exists and make another account's sequential ids
/// enumerable, which is the same reasoning as <c>MergeActivityEndpoint</c> and
/// <c>ActivityIdOwnershipExtensions</c>. A missing id and a foreign one are deliberately the same
/// answer.
/// </para>
/// <para>
/// Extends <c>IEndpoint</c> rather than a closed <c>Endpoint&lt;,&gt;</c> so the patch bases
/// (<c>Endpoint&lt;TRequest&gt;</c>) and the create/update bases (<c>Endpoint&lt;TRequest, long&gt;</c>)
/// can share it, and so <typeparamref name="TEntity"/> is the only type argument at a call site.
/// </para>
/// </remarks>
public static class TodoListReferenceOwnershipExtensions
{
    /// <summary>
    /// Passes when <paramref name="id"/> is null (an optional FK left unset references nothing) or names
    /// a <typeparamref name="TEntity"/> row visible to the caller. Otherwise answers 404 on
    /// <paramref name="field"/> and returns <c>false</c> — the hook contract's "response already sent".
    /// </summary>
    public static Task<bool> EnsureReferenceVisibleAsync<TEntity>(
        this IEndpoint endpoint, DbContext db, string field, long? id, CancellationToken ct)
        where TEntity : class, IEntityWithId
        => endpoint.AnswerAsync(db.AllVisibleAsync<TEntity>(ct, id), field, $"{typeof(TEntity).Name} not found.", ct);

    /// <summary>
    /// The <c>Activity</c> variant, which lifts the archived filter: pairing a task with an activity the
    /// user archived is legitimate, and 404ing it would turn "you archived this" into "this is not
    /// yours". Ownership is the only question here.
    /// </summary>
    public static Task<bool> EnsureActivityVisibleAsync(
        this IEndpoint endpoint, DbContext db, string field, long? id, CancellationToken ct)
        => endpoint.AnswerAsync(db.AllActivitiesVisibleAsync(ct, id), field, "Activity not found.", ct);

    private static async Task<bool> AnswerAsync(
        this IEndpoint endpoint, Task<bool> visible, string field, string message, CancellationToken ct)
    {
        if (await visible)
            return true;

        endpoint.ValidationFailures.Add(new ValidationFailure(field, message));
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, 404, cancellation: ct);
        return false;
    }
}
