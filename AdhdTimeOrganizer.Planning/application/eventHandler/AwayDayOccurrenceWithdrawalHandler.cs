using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.Planning.application.eventHandler;

/// <summary>
/// FO6's away gate, for a day marked away <b>after</b> its occurrence was placed: the materialiser passes over a
/// day that is already away, but today's and tomorrow's blocks are placed the moment a day-plan is read, so the
/// flu that starts tonight finds the gym block already on tomorrow. This removes it, when the user has not touched
/// it — see <see cref="IRepeatingTaskMaterialiser.WithdrawFromAwayDaysAsync"/>.
/// <para>
/// Planning both publishes and handles <see cref="CalendarDayTypesChangedEvent"/> here, beside Routines' own
/// handler, because the rows are Planning's and the event already says which days moved. A named class rather than
/// a second <c>CalendarDayTypesChangedEventHandler</c>, so the two slices' handlers are not told apart by namespace.
/// Resolves its own scope and never throws, like every cross-slice handler: the materialiser logs and swallows.
/// </para>
/// </summary>
public class AwayDayOccurrenceWithdrawalHandler(IServiceScopeFactory scopeFactory) : IEventHandler<CalendarDayTypesChangedEvent>
{
    public async Task HandleAsync(CalendarDayTypesChangedEvent eventModel, CancellationToken ct)
    {
        if (eventModel.Dates.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepeatingTaskMaterialiser>()
            .WithdrawFromAwayDaysAsync(eventModel.UserId, eventModel.Dates, ct);
    }
}
