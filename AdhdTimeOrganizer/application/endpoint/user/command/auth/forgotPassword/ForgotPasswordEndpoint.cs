using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.forgotPassword;

public class ForgotPasswordEndpoint(
    UserManager<User> userManager,
    IUserEmailSenderService<User> emailSender,
    IConfiguration configuration,
    IGoogleRecaptchaService googleRecaptchaService)
    : BaseForgotPasswordEndpoint<User>(userManager, emailSender, configuration, googleRecaptchaService);
