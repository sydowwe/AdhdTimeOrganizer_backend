using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.welcomeBack;

/// <summary>
/// <c>POST /welcome-back/dismiss</c> — FO5: closes the card without touching anything on it. Idempotent: with no card
/// open it is still a 204.
/// </summary>
public class DismissWelcomeBackEndpoint(AppDbContext dbContext) : EndpointWithoutRequest
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/welcome-back/dismiss");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Close the welcome-back card and leave everything on it as it is";
            s.Response(204, "Closed, or there was nothing to close");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        await dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.WelcomeBackAwayFrom != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WelcomeBackAwayFrom, (DateTime?)null)
                .SetProperty(u => u.WelcomeBackReturnedAt, (DateTime?)null), ct);

        await Send.NoContentAsync(ct);
    }
}
