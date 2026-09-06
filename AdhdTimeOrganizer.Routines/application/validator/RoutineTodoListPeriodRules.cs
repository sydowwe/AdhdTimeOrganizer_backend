using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentValidation;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.validator;

/// <summary>
/// The <c>TimePeriodId</c> half of the two routine-item write validators — ownership plus the
/// suggestion-bucket rule the period's length decides — shared so a message, a field name or a bucket
/// boundary cannot drift between create and update.
/// </summary>
/// <remarks>
/// <para><b>Why the ownership check exists.</b> <c>TimePeriodId</c> is client-supplied and written
/// straight onto the entity, and the FK enforces existence, not ownership: a request naming a
/// stranger's period inserted cleanly, stamped with the caller's own <c>UserId</c>. The attacker never
/// sees the row again — their own grouped read filters periods by <c>UserId</c> — so it is not a read
/// leak but a <b>write into the victim's data</b>: the reset job runs unauthenticated with the global
/// filter inert, pulls the foreign item into the victim's period and counts it, so a permanently
/// unticked item breaks a streak the victim cannot see the cause of, and the
/// <c>RoutinePeriodCompletion</c> rows it writes are wrong for good.</para>
///
/// <para>Same message for "does not exist" and "is not yours", or the endpoint becomes an existence
/// oracle over another account's id range — the rule <c>LookupOwnership</c> and the tracker-mapping
/// guard already follow.</para>
///
/// <para>The user id comes off <see cref="IHttpContextAccessor"/> and the <c>DbContext</c> out of a
/// fresh scope because FastEndpoints validators are singletons; the explicit <c>UserId</c> comparison
/// is what decides, so this does not depend on how that scope's <c>ScopeUserId</c> is populated.</para>
/// </remarks>
internal static class RoutineTodoListPeriodRules
{
    private const string PeriodNotFound = "Time period not found.";

    public static async Task ApplyAsync<TRequest>(
        ValidationContext<TRequest> ctx,
        long timePeriodId,
        IReadOnlyCollection<DayOfWeek> suggestedDays,
        int? suggestedDayOfMonth,
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor http,
        CancellationToken ct)
    {
        var userId = http.HttpContext!.User.GetId();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();

        var lengthInDays = await db.Set<RoutineTimePeriod>()
            .Where(p => p.Id == timePeriodId && p.UserId == userId)
            .Select(p => (int?)p.LengthInDays)
            .FirstOrDefaultAsync(ct);

        if (lengthInDays is null)
        {
            ctx.AddFailure(nameof(CreateRoutineTodoListRequest.TimePeriodId), PeriodNotFound);
            return;
        }

        if (lengthInDays <= 1)
        {
            if (suggestedDays.Count > 0)
                ctx.AddFailure(nameof(CreateRoutineTodoListRequest.SuggestedDays), "SuggestedDays must be empty for daily periods.");
            if (suggestedDayOfMonth.HasValue)
                ctx.AddFailure(nameof(CreateRoutineTodoListRequest.SuggestedDayOfMonth), "SuggestedDayOfMonth must be null for daily periods.");
        }
        else if (lengthInDays <= 14)
        {
            if (suggestedDayOfMonth.HasValue)
                ctx.AddFailure(nameof(CreateRoutineTodoListRequest.SuggestedDayOfMonth), "SuggestedDayOfMonth is only allowed for monthly+ periods (LengthInDays > 14).");
        }
        else
        {
            if (suggestedDays.Count > 0)
                ctx.AddFailure(nameof(CreateRoutineTodoListRequest.SuggestedDays), "SuggestedDays is only allowed for weekly periods (LengthInDays ≤ 14).");
        }
    }
}
