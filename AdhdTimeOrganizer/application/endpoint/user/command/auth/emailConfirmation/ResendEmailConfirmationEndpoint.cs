using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ResendConfirmationEmailEndpoint(
    IUserEmailSenderService<User> emailSender,
    UserManager<User> userManager,
    IDistributedCache cache)
    : BaseResendConfirmationEmailEndpoint<User>(emailSender, userManager, cache)
{
}
