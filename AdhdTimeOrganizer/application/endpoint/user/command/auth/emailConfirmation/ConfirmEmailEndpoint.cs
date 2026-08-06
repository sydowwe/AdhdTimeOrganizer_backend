using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ConfirmEmailEndpoint(UserManager<User> userManager)
    : BaseConfirmEmailEndpoint<User>(userManager)
{
}