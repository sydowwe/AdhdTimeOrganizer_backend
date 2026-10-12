using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.service.welcomeBack;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Routines.domain.service;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.welcomeBack;

/// <summary>
/// <c>GET /welcome-back</c> — FO5's card: what piled up while the user was gone, so home can turn it into three
/// decisions instead of a wall. 204 when there is no card (see <see cref="WelcomeBackResponse"/>).
/// <para>
/// The card is opened by <c>LastActiveStampMiddleware</c> on the first request after an absence of at least
/// <c>UserPresence.GreetAfter</c>, before this or any endpoint runs, so it does not matter which of the client's
/// first requests this is. Reading it changes nothing: the card stays until it is answered
/// (<c>POST /welcome-back/fresh-start</c>), dismissed (<c>POST /welcome-back/dismiss</c>) or lapses.
/// </para>
/// </summary>
public class GetWelcomeBackEndpoint(AppDbContext dbContext, ICalendarDayLookup calendarDays, TimeProvider time)
    : EndpointWithoutRequest<WelcomeBackResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/welcome-back");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "The welcome-back card, if the user just came back from an absence";
            s.Description = "Overdue to-dos, planner blocks left open since the absence began, routines whose streak "
                            + "ended meanwhile, and the unread count — each with a short preview. 204 when there is no card.";
            s.Response<WelcomeBackResponse>(200, "The card");
            s.Response(204, "No card: not away long enough, already answered or dismissed, or lapsed");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var window = await WelcomeBackPile.OpenWindowAsync(dbContext, userId, time.GetUtcNow().UtcDateTime, ct);
        if (window is null)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var todos = WelcomeBackPile.OverdueTodos(dbContext, userId, window).AsNoTracking();
        var tasks = WelcomeBackPile.PastTasks(dbContext, userId, window).AsNoTracking();

        // Each with its whole history: whether a skip can still bring the run back is DescribeBreak's answer, the
        // same one the broken-streak notification and the freeze endpoint give, so the card cannot promise a
        // rescue the routines page would then refuse. A handful of routines, so the walk is cheap.
        var broken = await WelcomeBackPile.BrokenRoutines(dbContext, userId, window)
            .AsNoTracking()
            .Include(p => p.CompletionHistoryColl)
            .ToListAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;

        var response = new WelcomeBackResponse
        {
            AwayFrom = window.AwayFrom,
            ReturnedAt = window.ReturnedAt,
            DaysAway = window.ReturnDate.DayNumber - window.AwayDate.DayNumber,
            WasMarkedAway = await WasMarkedAwayAsync(userId, window, ct),
            OverdueTodos = new WelcomeBackPileResponse
            {
                Count = await todos.CountAsync(ct),
                Preview = await todos
                    .Take(WelcomeBackPile.PreviewSize)
                    .Select(i => new WelcomeBackItemResponse(i.Id, i.Activity.Name, i.DueDate!.Value))
                    .ToListAsync(ct)
            },
            PastTasks = new WelcomeBackPileResponse
            {
                Count = await tasks.CountAsync(ct),
                Preview = await tasks
                    .Take(WelcomeBackPile.PreviewSize)
                    .Select(t => new WelcomeBackItemResponse(t.Id, t.Activity.Name, t.Calendar.Date))
                    .ToListAsync(ct)
            },
            BrokenRoutines = broken
                .Select(p => new WelcomeBackRoutineResponse(p.Id, p.Text, p.Color,
                    RoutineStreakFreezeService.DescribeBreak(
                        p, p.CompletionHistoryColl.OrderBy(c => c.PeriodStart).ToList(), now).CanFreeze))
                .ToList(),
            UnreadNotifications = await WelcomeBackPile.UnreadNotifications(dbContext, userId).CountAsync(ct)
        };

        await Send.OkAsync(response, ct);
    }

    /// <summary>
    /// FO1's rule over the days the user was entirely gone — after the day they left, before the day they came back:
    /// at least half of them marked away. The same <c>RoutineAwayRule</c> that excuses a routine cycle, so "you were
    /// on holiday" means the same thing on the card as it does to a streak.
    /// </summary>
    private async Task<bool> WasMarkedAwayAsync(long userId, WelcomeBackPile.Window window, CancellationToken ct)
    {
        var first = window.AwayDate.AddDays(1);
        var last = window.ReturnDate.AddDays(-1);
        if (last < first)
            return false;

        var awayDates = await calendarDays.GetAwayDatesAsync(dbContext, [userId], first, last, ct);
        return RoutineAwayRule.IsMostlyAway(awayDates.GetValueOrDefault(userId), first, window.ReturnDate);
    }
}
