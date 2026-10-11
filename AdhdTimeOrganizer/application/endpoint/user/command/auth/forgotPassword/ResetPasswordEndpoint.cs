using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.forgotPassword;

public class ResetPasswordEndpoint(
    UserManager<User> userManager,
    IRefreshTokenService refreshTokenService,
    IGoogleRecaptchaService googleRecaptchaService,
    ISecurityAlertService<User> securityAlerts)
    : BaseResetPasswordEndpoint<User>(userManager, refreshTokenService, googleRecaptchaService, securityAlerts);
