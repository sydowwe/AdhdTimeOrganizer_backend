using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class UpdateUserPreferencesEndpoint(UserManager<User> userManager) : Endpoint<UpdateUserPreferencesRequest>
{
    public override void Configure()
    {
        Put("user/preferences");
        Validator<UpdateUserPreferencesValidator>();
        Summary(s => { s.Summary = "Partially update the current user's appearance and locale preferences"; });
    }

    public override async Task HandleAsync(UpdateUserPreferencesRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (req.Theme.HasValue) user.Theme = req.Theme.Value;
        if (req.Locale.HasValue) user.CurrentLocale = req.Locale.Value;
        if (req.FirstDayOfWeek.HasValue) user.FirstDayOfWeek = req.FirstDayOfWeek.Value;
        if (req.AskBeforeDelete.HasValue) user.AskBeforeDelete = req.AskBeforeDelete.Value;

        if (req.Timezone != null)
            user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
