using System.Linq.Expressions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Humanizer;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public abstract class BaseChangeDisplayOrderTodoListEndpoint<TEntity>(AppCommandDbContext dbContext, IOptions<TodoListSettings> settings) : Endpoint<ChangeDisplayOrderRequest>
    where TEntity : BaseTodoList
{
    private readonly DbSet<TEntity> _dbSet = dbContext.Set<TEntity>();
    private readonly TodoListSettings _settings = settings.Value;


    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;

        Patch($"{entityName.Kebaberize()}/change-display-order");
        AllowAnonymous();

        Summary(s =>
        {
            s.Summary = $"Reorders a single {entityName} item within a list.";
            s.Description = "This endpoint calculates a new 'DisplayOrder' for an item based on its preceding and following siblings.";
            s.Responses[200] = "The item was successfully reordered.";
            s.Responses[404] = "The item to move, or a sibling item, could not be found.";
            s.Responses[409] = "A conflict occurred, requiring a full list re-index.";
            s.Responses[400] = "A bad request or other validation error occurred.";
        });
    }

    /// <summary>
    /// Handles the incoming reorder request.
    /// </summary>
    public override async Task HandleAsync(ChangeDisplayOrderRequest req, CancellationToken ct)
    {
        var itemToMove = await _dbSet
            .FirstOrDefaultAsync(x => x.Id == req.MovedItemId, ct);

        if (itemToMove is null)
        {
            AddError("The item to be moved could not be found.");
            await SendErrorsAsync(404, ct);
            return;
        }


        await using (var tx = await dbContext.Database.BeginTransactionAsync(ct))
        {
            // Attempt to calculate new order. If conflict triggers, we'll rebalance and retry once.
            var newOrderResult = await CalculateNewOrderAsync(req, ct);

            if (newOrderResult.Failed)
            {
                AddError(newOrderResult.ErrorMessage ?? "An error occurred during order calculation.");
                var statusCode = MapErrorTypeToStatusCode(newOrderResult.ErrorType);
                await SendErrorsAsync(statusCode, ct);
                return;
            }

            itemToMove.DisplayOrder = newOrderResult.Data;
            var result = await dbContext.UpdateEntityAsync(itemToMove, ct);

            if (result.Failed)
            {
                AddError(result.ErrorMessage ?? "An error occurred during update.");
                await SendErrorsAsync(500, ct);
                return;
            }

            await tx.CommitAsync(ct);
        }

        await SendOkAsync(ct);
    }

    /// <summary>
    /// Maps a domain error type to a corresponding HTTP status code.
    /// </summary>
    private static int MapErrorTypeToStatusCode(ResultErrorType? errorType)
    {
        return errorType switch
        {
            ResultErrorType.NotFound => 404,
            ResultErrorType.Conflict => 409,
            _ => 400 // Default to 400 Bad Request for other errors
        };
    }

    /// <summary>
    /// Calculates the new DisplayOrder value based on the surrounding items using arithmetic of precedingOrder and followingOrder.
    /// </summary>
    private async Task<Result<long>> CalculateNewOrderAsync(ChangeDisplayOrderRequest req, CancellationToken ct)
    {
        var standardGap = _settings.DisplayOrderGap;

        // Case 1: Move to the top of the list
        if (!req.PrecedingItemId.HasValue)
        {
            // If the list is completely empty (no following), start with startOrder.
            if (!req.FollowingItemId.HasValue)
            {
                return Result.Successful(standardGap);
            }

            var followingOrder = await _dbSet.GetDisplayOrderById(req.FollowingItemId.Value, ct);

            return followingOrder.HasValue
                ? Result.Successful(ArithmeticNewOrderNumber(followingOrder.Value - standardGap, followingOrder.Value))
                : Result<long>.Error(ResultErrorType.NotFound, "The specified 'FollowingItemId' was not found.");
        }
        if (!req.FollowingItemId.HasValue)
        {
            var precedingOrder = await _dbSet.GetDisplayOrderById(req.PrecedingItemId.Value, ct);

            return precedingOrder.HasValue
                ? Result.Successful(ArithmeticNewOrderNumber(precedingOrder.Value, precedingOrder.Value + standardGap))
                : Result<long>.Error(ResultErrorType.NotFound, "The specified 'PrecedingItemId' was not found.");
        }

        // Case 3: Move between two existing items
        var preOrder = await _dbSet.GetDisplayOrderById(req.PrecedingItemId.Value, ct);
        if (!preOrder.HasValue)
            return Result<long>.Error(ResultErrorType.NotFound, "The specified 'PrecedingItemId' was not found.");

        var folOrder = await _dbSet.GetDisplayOrderById(req.FollowingItemId.Value, ct);
        if (!folOrder.HasValue)
            return Result<long>.Error(ResultErrorType.NotFound, "The specified 'FollowingItemId' was not found.");

        var newOrder = ArithmeticNewOrderNumber(preOrder.Value, folOrder.Value);

        // If no gap (midpoint equals one of the endpoints) -> signal conflict and request rebalance
        if (newOrder == preOrder.Value || newOrder == folOrder.Value)
        {
            await RebalanceDisplayOrdersAsync(req.FollowingItemId.Value,ct);
            var newOrderResult = await CalculateNewOrderAsync(req, ct);
            return newOrderResult;
        }
        return Result.Successful(newOrder);
    }

    protected abstract Expression<Func<TEntity, long>> GroupFilterExpression { get; }


    private async Task RebalanceDisplayOrdersAsync(long followingId, CancellationToken ct)
    {
        var groupId = await _dbSet.GetGroupIdById(followingId, GroupFilterExpression, ct);
        if (groupId is null)
        {
            return;
        }

        var query = _dbSet.AsQueryable()
            .Where(e => e.UserId == User.GetId());

        var items = await query.Where(e => GroupFilterExpression.Compile()(e) == groupId)
            .OrderByDescending(e => e.DisplayOrder)
            .ToListAsync(ct);

        for (var i = 0; i < items.Count; i++)
        {
            items[i].DisplayOrder = _settings.DisplayOrderStart - (i + 1) * _settings.DisplayOrderGap;
        }

        dbContext.Set<TEntity>().UpdateRange(items);


        await dbContext.SaveChangesAsync(ct);
    }

    private static long ArithmeticNewOrderNumber(long precedingOrder, long followingOrder) => precedingOrder + (followingOrder - precedingOrder) / 2;
}