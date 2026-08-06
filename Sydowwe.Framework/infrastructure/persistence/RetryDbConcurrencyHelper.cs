using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.infrastructure.persistence;

public static class RetryDbConcurrencyHelper
{
    private const int DefaultMaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(50);
    private static readonly Random Jitter = Random.Shared;

    extension(DbContext dbContext)
    {
        /// <param name="retryOnUniqueViolation">
        /// When true, a unique-constraint violation (<c>23505</c>) is treated like a concurrency conflict
        /// and retried: the transaction rolls back, the ChangeTracker is cleared and the operation re-runs
        /// on fresh data. Only safe when the operation re-derives the conflicting value each attempt
        /// (e.g. <c>Max(Version) + 1</c>); otherwise the retry just reproduces the same clash. On the final
        /// failed attempt a clean <see cref="ResultErrorType.DbUniqueViolationError"/> is returned instead
        /// of a raw <see cref="ResultErrorType.DatabaseError"/> (500).
        /// </param>
        public async Task<Result<T>> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<Result<T>>> operation,
            CancellationToken ct,
            int maxRetries = DefaultMaxRetries,
            bool retryOnUniqueViolation = false) where T : notnull
        {
            var logger = ((IInfrastructure<IServiceProvider>)dbContext).Instance
                         .GetService<ILogger<DbContext>>()
                         ?? NullLogger<DbContext>.Instance;

            for (var attempt = 1;; attempt++)
            {
                if (attempt > 1)
                    dbContext.ChangeTracker.Clear();

                await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await operation(ct);

                    if (result.Failed)
                    {
                        await tx.RollbackAsync(ct);
                        return result;
                    }

                    await tx.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException ex) when (attempt < maxRetries)
                {
                    await tx.RollbackAsync(ct);
                    logger.LogWarning(
                        ex,
                        "Concurrency conflict on attempt {Attempt}/{MaxRetries}. Retrying after delay...",
                        attempt, maxRetries);
                    // Exponential back-off with jitter: 50ms, 100ms, 200ms … ± random noise
                    var delay = BaseDelay * Math.Pow(2, attempt - 1)
                                + TimeSpan.FromMilliseconds(Jitter.Next(0, 30));
                    await Task.Delay(delay, ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await tx.RollbackAsync(ct);
                    return Result<T>.Error(
                        ResultErrorType.DbConcurrencyError,
                        $"Concurrency conflict persisted after {maxRetries} attempts. Please retry.");
                }
                catch (DbUpdateException ex) when (retryOnUniqueViolation && IsUniqueViolation(ex) && attempt < maxRetries)
                {
                    await tx.RollbackAsync(ct);
                    logger.LogWarning(
                        ex,
                        "Unique-constraint conflict on attempt {Attempt}/{MaxRetries}. Retrying after delay...",
                        attempt, maxRetries);
                    var delay = BaseDelay * Math.Pow(2, attempt - 1)
                                + TimeSpan.FromMilliseconds(Jitter.Next(0, 30));
                    await Task.Delay(delay, ct);
                }
                catch (DbUpdateException ex) when (retryOnUniqueViolation && IsUniqueViolation(ex))
                {
                    await tx.RollbackAsync(ct);
                    return Result<T>.Error(
                        ResultErrorType.DbUniqueViolationError,
                        $"Unique-constraint conflict persisted after {maxRetries} attempts. Please retry.",
                        ex.Message);
                }
                catch (OperationCanceledException)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Result<T>.Error(ResultErrorType.DatabaseError, "Operation failed.", ex.Message);
                }
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}