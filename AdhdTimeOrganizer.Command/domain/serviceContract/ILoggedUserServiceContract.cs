using System.Security.Claims;
using AdhdTimeOrganizer.Command.domain.model.entity.user;

namespace AdhdTimeOrganizer.Command.domain.serviceContract;

public interface ILoggedUserServiceContract
{
    ClaimsPrincipal? LoggedUserPrincipal { get; }
    LoggedUser? LoggedUser { get; }
    bool IsAuthenticated { get; }
    long LoggedUserId { get; }
}