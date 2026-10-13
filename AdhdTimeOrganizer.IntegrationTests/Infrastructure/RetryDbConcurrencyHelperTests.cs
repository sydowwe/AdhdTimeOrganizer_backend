using FluentAssertions;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="RetryDbConcurrencyHelper.ExecuteWithRetryAsync{T}"/>'s second way of reporting a lost race: a failed
/// <see cref="Result{T}"/> of type <see cref="ResultErrorType.DbConcurrencyError"/>, which is how a writer that catches
/// the exception itself (Identity's <c>UserManager.UpdateAsync</c>) hands the conflict back. What it must not do is
/// retry every failure — a validation error re-run three times is three times the same answer, slower.
/// The end-to-end case is <c>UserPreferencesConcurrencyTests</c>.
/// </summary>
[Collection("Postgres")]
public class RetryDbConcurrencyHelperTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "A conflict reported as a Result is retried until the operation succeeds")]
    public async Task AConflictResult_IsRetried_UntilItSucceeds()
    {
        await using var db = CreateDbContext();
        var calls = 0;

        var result = await db.ExecuteWithRetryAsync(_ =>
        {
            calls++;
            return Task.FromResult(calls < 3
                ? Result<int>.Error(ResultErrorType.DbConcurrencyError, "moved")
                : Result<int>.Successful(calls));
        }, CancellationToken);

        result.Failed.Should().BeFalse();
        result.Data.Should().Be(3);
    }

    [Fact(DisplayName = "A conflict reported as a Result on every attempt ends in DbConcurrencyError after maxRetries")]
    public async Task AConflictResultOnEveryAttempt_GivesUpAfterMaxRetries()
    {
        await using var db = CreateDbContext();
        var calls = 0;

        var result = await db.ExecuteWithRetryAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result<int>.Error(ResultErrorType.DbConcurrencyError, "moved"));
        }, CancellationToken, maxRetries: 2);

        calls.Should().Be(2);
        result.ErrorType.Should().Be(ResultErrorType.DbConcurrencyError, "a lost race is a 409 however it was reported");
    }

    [Fact(DisplayName = "Any other failed Result is returned as it is, without a retry")]
    public async Task AnyOtherFailure_IsNotRetried()
    {
        await using var db = CreateDbContext();
        var calls = 0;

        var result = await db.ExecuteWithRetryAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result<int>.Error(ResultErrorType.IdentityError, "bad email"));
        }, CancellationToken);

        calls.Should().Be(1);
        result.ErrorType.Should().Be(ResultErrorType.IdentityError);
        result.ErrorMessage.Should().Be("bad email");
    }
}
