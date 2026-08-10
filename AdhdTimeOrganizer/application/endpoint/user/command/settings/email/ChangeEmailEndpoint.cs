using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings.email;

public class ChangeEmailEndpoint(
    UserManager<User> userManager,
    IUserEmailSenderService<User> emailSender)
    : BaseChangeEmailEndpoint<User>(userManager, emailSender)
{
}