using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class RegisterUserEndpoint(
    UserManager<User> userManager,
    DbContext dbContext,
    IGoogleRecaptchaService googleRecaptchaService,
    ITwoFactorAuthService<User> twoFactorAuthService,
    IUserEmailSenderService<User> emailSender,
    IUserDefaultsService userDefaultsService
) : BaseRegisterUserEndpoint<User, PasswordRegistrationRequest>(
    userManager, dbContext, googleRecaptchaService, twoFactorAuthService, emailSender, userDefaultsService);