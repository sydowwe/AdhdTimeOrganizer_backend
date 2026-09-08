using System.Linq.Expressions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using FastEndpoints;
using Humanizer;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;

public abstract class BaseChangeDisplayOrderTodoListEndpoint<TEntity>(DbContext dbContext, IOptions<TodoListSettings> settings) : Endpoint<ChangeDisplayOrderRequest>
    where TEntity : BaseTodoListItem
{
    private readonly DbSet<TEntity> _dbSet = dbContext.Set<TEntity>();
    private readonly TodoListSettings _settings = settings.Value;


    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;

        Patch($"/{entityName.Kebaberize()}/change-display-order");


        Summary(s =>
        {
            s.Summary = $"Reorders a single {entityName} item within a list.";
            s.Description = "This endpoint calculates a new 'DisplayOrder' for an item based on its preceding and following siblings.";
            s.Responses[204] = "The item was successfully reordered.";
            s.Responses[404] = "The item to move, or a sibling item, could not be found.";
            s.Responses[409] = "A conflict occurred, requiring a full list re-index.";
            s.Responses[400] = "A bad request or other validation error occurred.";
            s.Responses[500] = "An error occurred during item update.";
        });
    }

    /// <summary>
    /// Handles the incoming reorder request.
    /// </summary>
    public override async Task HandleAsync(ChangeDisplayOrderRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // userId is defense-in-depth alongside the global IEntityWithUser query filter, matching what
        // GetDisplayOrderById/GetGroupIdById do for every sibling lookup in this same request: the
        // filter must not be the only thing standing between this and a cross-user row.
        var itemToMove = await _dbSet
            .FirstOrDefaultAsync(x => x.Id == req.MovedItemId && x.UserId == userId, ct);

        if (itemToMove is null)
        {
            AddError("The item to be moved could not be found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }


        await using (var tx = await dbContext.Database.BeginTransactionAsync(ct))
        {
            // Attempt to calculate new order. If conflict triggers, we'll rebalance and retry once.
            var newOrderResult = await CalculateNewOrderAsync(req, userId, false, ct);

            if (newOrderResult.Failed)
            {
                AddError(newOrderResult.ErrorMessage ?? "An error occurred during order calculation.");
                await Send.ErrorsAsync(EndpointHelper.ToStatusCode(newOrderResult.ErrorType), ct);
                return;
            }

            itemToMove.DisplayOrder = newOrderResult.Data;
            var result = await dbContext.UpdateEntityAsync(itemToMove, ct);

            if (result.Failed)
            {
                AddError(result.ErrorMessage ?? "An error occurred during update.");
                await Send.ErrorsAsync(500, ct);
                return;
            }

            await tx.CommitAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }

    /// <summary>
    /// Calculates the new DisplayOrder value based on the surrounding items using arithmetic of precedingOrder and followingOrder.
    /// <para>
    /// <paramref name="alreadyRebalanced"/> is what keeps the retry a retry. The recursion below re-enters with
    /// identical arguments, so it only terminates if the rebalance actually opened a gap — and
    /// <see cref="RebalanceDisplayOrdersAsync"/> returns early, doing nothing, whenever the following item's group
    /// id cannot be read. Without the flag that case recurses until the stack overflows and takes the process down;
    /// with it, the second failure answers the 409 the <c>Summary</c> block already advertises.
    /// </para>
    /// </summary>
    private async Task<Result<long>> CalculateNewOrderAsync(ChangeDisplayOrderRequest req, long userId, bool alreadyRebalanced, CancellationToken ct)
    {
        var standardGap = _settings.DisplayOrderGap;

        // Case 1: Move to the top of the list
        if (!req.PrecedingItemId.HasValue)
        {
            // If the list is completely empty (no following), start where creation starts:
            // GetNextDisplayOrder hands the first row of a group DisplayOrderStart, and the scheme counts
            // down from there by DisplayOrderGap. Returning the gap itself would give the same column two
            // different "first item" values depending on which route wrote it.
            if (!req.FollowingItemId.HasValue)
                return Result.Successful(_settings.DisplayOrderStart);

            var followingOrder = await _dbSet.GetDisplayOrderById(req.FollowingItemId.Value, userId, ct);

            return followingOrder.HasValue
                ? Result.Successful(ArithmeticNewOrderNumber(followingOrder.Value - standardGap, followingOrder.Value))
                : Result<long>.Error(ResultErrorType.NotFound, "The specified 'FollowingItemId' was not found.");
        }

        if (!req.FollowingItemId.HasValue)
        {
            var precedingOrder = await _dbSet.GetDisplayOrderById(req.PrecedingItemId.Value, userId, ct);

            return precedingOrder.HasValue
                ? Result.Successful(ArithmeticNewOrderNumber(precedingOrder.Value, precedingOrder.Value + standardGap))
                : Result<long>.Error(ResultErrorType.NotFound, "The specified 'PrecedingItemId' was not found.");
        }

        // Case 3: Move between two existing items
        var preOrder = await _dbSet.GetDisplayOrderById(req.PrecedingItemId.Value, userId, ct);
        if (!preOrder.HasValue)
            return Result<long>.Error(ResultErrorType.NotFound, "The specified 'PrecedingItemId' was not found.");

        var folOrder = await _dbSet.GetDisplayOrderById(req.FollowingItemId.Value, userId, ct);
        if (!folOrder.HasValue)
            return Result<long>.Error(ResultErrorType.NotFound, "The specified 'FollowingItemId' was not found.");

        var newOrder = ArithmeticNewOrderNumber(preOrder.Value, folOrder.Value);

        // If no gap (midpoint equals one of the endpoints) -> signal conflict and request rebalance
        if (newOrder == preOrder.Value || newOrder == folOrder.Value)
        {
            if (alreadyRebalanced)
                return Result<long>.Error(ResultErrorType.Conflict,
                    "The items around the requested position have no gap left even after a re-index. The list needs a full re-index.");

            await RebalanceDisplayOrdersAsync(req.FollowingItemId.Value, userId, ct);
            return await CalculateNewOrderAsync(req, userId, true, ct);
        }

        return Result.Successful(newOrder);
    }

    protected abstract Expression<Func<TEntity, long>> GroupFilterExpression { get; }

    /// <summary>
    /// <see cref="GroupFilterExpression"/> as a predicate EF can translate.
    /// <para>
    /// Composing the tree by hand is not a style choice. The obvious spelling —
    /// <c>Where(e =&gt; GroupFilterExpression.Compile()(e) == groupId)</c> — puts a <b>compiled delegate</b>
    /// inside the expression tree: EF evaluates the <c>Compile()</c> call client-side (it does not involve the
    /// parameter), is left with <c>Invoke(delegate, e)</c>, and cannot translate that, so the whole rebalance
    /// throws <see cref="InvalidOperationException"/> and the reorder answers 500.
    /// </para>
    /// <para>
    /// The group id is spliced in as a captured closure member rather than an <c>Expression.Constant</c> so
    /// EF still parameterises it — a literal would give every group its own entry in the query plan cache.
    /// </para>
    /// </summary>
    private Expression<Func<TEntity, bool>> InGroupExpression(long groupId)
    {
        Expression<Func<long>> groupIdRef = () => groupId;
        var groupFilter = GroupFilterExpression;

        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(groupFilter.Body, groupIdRef.Body),
            groupFilter.Parameters[0]);
    }

    private async Task RebalanceDisplayOrdersAsync(long followingId, long userId, CancellationToken ct)
    {
        var groupId = await _dbSet.GetGroupIdById(followingId, userId, GroupFilterExpression, ct);
        if (groupId is null)
            return;

        var query = _dbSet.AsQueryable()
            .Where(e => e.UserId == userId);

        var items = await query.Where(InGroupExpression(groupId.Value))
            .OrderByDescending(e => e.DisplayOrder)
            .ToListAsync(ct);

        for (var i = 0; i < items.Count; i++)
            items[i].DisplayOrder = _settings.DisplayOrderStart - (i + 1) * _settings.DisplayOrderGap;

        dbContext.Set<TEntity>().UpdateRange(items);


        await dbContext.SaveChangesAsync(ct);
    }

    private static long ArithmeticNewOrderNumber(long precedingOrder, long followingOrder) => precedingOrder + (followingOrder - precedingOrder) / 2;
}