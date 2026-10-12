using AdhdTimeOrganizer.application.service.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.settings;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Account deletion, plus the module half of the erasure the DB cascade cannot reach — see
/// <see cref="IUserAccountDeletionService"/>, which is also what purges a sign-up that was never confirmed, so the two
/// deletions cannot drift apart.
/// </summary>
public class DeleteUserAccountEndpoint(
    UserManager<User> userManager,
    IUserAccountDeletionService accountDeletionService)
    : BaseDeleteUserAccountEndpoint<User>(userManager)
{
    /// <summary>
    /// Stages the module erasure before the user row goes, while their rows are still readable; the base's
    /// <c>UserManager.DeleteAsync</c> then commits it together with the deletion. Exceptions are deliberately not
    /// caught — the base aborts the deletion when this hook throws.
    /// </summary>
    protected override Task BeforeDeleteAsync(User user, CancellationToken ct) =>
        accountDeletionService.StageModuleErasureAsync(user, ct);
}
