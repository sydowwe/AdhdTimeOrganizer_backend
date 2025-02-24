using System.Linq.Expressions;
using AdhdTimeOrganizer.Command.application.dto.request.user;
using AdhdTimeOrganizer.Command.application.dto.response.user;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Command.domain.serviceContract;
using AdhdTimeOrganizer.Command.infrastructure.extService;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.application.service;
using AdhdTimeOrganizer.Common.domain.exception;
using AdhdTimeOrganizer.Common.domain.helper;
using AdhdTimeOrganizer.Common.domain.model.@enum;
using AdhdTimeOrganizer.Common.domain.result;
using AdhdTimeOrganizer.Common.infrastructure.extension;
using AutoMapper;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace AdhdTimeOrganizer.Command.application.service.user;

public class UserService(
    AppCommandDbContext dbContext,
    UserManager<UserEntity> userManager,
    SignInManager<UserEntity> signInManager,
    ILoggedUserService loggedUserService,
    IGoogleRecaptchaService googleRecaptchaService,
    IUserEmailSenderService emailSender,
    IMapper mapper,
    IUserSessionService userSessionService,
    IMediator mediator,
    IConfiguration configuration,
    ILogger<UserService> logger) : BaseService<UserEntity>(mapper), IUserService
{
    private readonly DbSet<UserEntity> _users = dbContext.Set<UserEntity>();

    public async Task<ServiceResult> ChangeCurrentLocaleAsync(AvailableLocales locale)
    {
        var user = await GetCurrentUserAsync();
        user.CurrentLocale = locale;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? ServiceResult.Successful()
            : ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());
    }

    #region Authorization

    public async Task<ServiceResult<TwoFactorAuthResponse>> RegisterAsync(RegistrationRequest registration)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(registration.RecaptchaToken, "register");
        if (recaptchaResult.Failed)
            return ServiceResult<TwoFactorAuthResponse>.Error(recaptchaResult);
        var newUser = mapper.Map<UserEntity>(registration);
        var defaultSettingsResult = SetDefaultSettingsAsync(newUser);
        if (defaultSettingsResult.Failed)
        {
            return ServiceResult<TwoFactorAuthResponse>.Error(defaultSettingsResult);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.CreateAsync(newUser, registration.Password);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail")
                ? ServiceResult<TwoFactorAuthResponse>.Error(ServiceErrorType.Conflict,
                    "User already exists with EMAIL: " + newUser.Email)
                : ServiceResult<TwoFactorAuthResponse>.Error(ServiceErrorType.BadRequest,
                    "Failed to register user because: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        ServiceResult<TwoFactorAuthResponse> response;
        try
        {
            await SendConfirmationEmail(newUser);
            response = await SetUpTwoFactorAuth(newUser);
            await mediator.Publish(new UserRegisteredEvent(newUser.Id));
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            logger.LogInformation(e, "Registration failed while setting things up for user");
            return ServiceResult<TwoFactorAuthResponse>.Error(ServiceErrorType.InternalServerError, e.Message);
        }

        await transaction.CommitAsync();

        return response;
    }

    private static async Task<ServiceResult<string>> GetEmailFromGoogleSignInCode(string code)
    {
        var tokenResponse = await new AuthorizationCodeInstalledApp(
            new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_ID"),
                    ClientSecret = Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_SECRET")
                }
            }),
            new LocalServerCodeReceiver()
        ).Flow.ExchangeCodeForTokenAsync("user", code, "<Your Redirect URI>", CancellationToken.None);
        var idToken = tokenResponse?.IdToken;
        if (idToken == null)
            return ServiceResult<string>.Error(ServiceErrorType.BadRequest, "Invalid Google login code");

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken);
        var email = payload.Email;
        return email == null
            ? ServiceResult<string>.Error(ServiceErrorType.BadRequest, "Email not found in token")
            : ServiceResult<string>.Successful(email);
    }

    public async Task<ServiceResult<GoogleSignInResponse>> GoogleSignInAsync(GoogleSignInRequest googleSignInRequest)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(googleSignInRequest.RecaptchaToken, "login");
        if (recaptchaResult.Failed)
            return ServiceResult<GoogleSignInResponse>.Error(recaptchaResult);
        var emailResult = await GetEmailFromGoogleSignInCode(googleSignInRequest.Code);
        if (emailResult.Failed)
            return ServiceResult<GoogleSignInResponse>.Error(emailResult);
        var email = emailResult.Data.ToLower();
        var userResult = await GetByEmailAsync(email);
        if (userResult.Failed)
            return ServiceResult<GoogleSignInResponse>.Error(userResult);

        var user = userResult.Data;
        await signInManager.SignInAsync(user, googleSignInRequest.StayLoggedIn, "Google");
        // user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(googleSignInRequest.Timezone);
        await userManager.UpdateAsync(user);
        return ServiceResult<GoogleSignInResponse>.Successful(
            new GoogleSignInResponse
            {
                Email = email,
                CurrentLocale = user.CurrentLocale
            }
        );
    }

    public async Task<ServiceResult<LoginResponse>> LoginAsync(PasswordLoginRequest loginRequest)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(loginRequest.RecaptchaToken, "login");
        if (recaptchaResult.Failed)
            return ServiceResult<LoginResponse>.Error(recaptchaResult);
        var userResult = await GetUserByEmailWithIncludes(loginRequest.Email.ToLower());
        if (userResult.Failed)
            return ServiceResult<LoginResponse>.Error(userResult);

        var user = userResult.Data;
        var result = await signInManager.PasswordSignInAsync(user, loginRequest.Password,
            loginRequest.StayLoggedIn, true);
        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.Now;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            return ServiceResult<LoginResponse>.Error(ServiceErrorType.UserLockedOut,
                $"User locked out for {minutes}m {seconds}s");
        }

        if (result.IsNotAllowed)
        {
            if (!user.EmailConfirmed)
                return ServiceResult<LoginResponse>.Error(ServiceErrorType.EmailNotConfirmed,
                    "Confirm your email before logging in");
            await userManager.AccessFailedAsync(user);
            return ServiceResult<LoginResponse>.Error(ServiceErrorType.AuthenticationFailed,
                "Wrong email or password");
        }

        if (result is { Succeeded: false, RequiresTwoFactor: false })
            return ServiceResult<LoginResponse>.Error(ServiceErrorType.InternalServerError, result.ToString());
        // user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(loginRequest.Timezone);
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ServiceResult<LoginResponse>.Error(ServiceErrorType.InternalServerError,
                "Failed to update user");
        }

        userSessionService.UserLogin(user.CurrentLocale, loginRequest.Timezone);
        return ServiceResult<LoginResponse>.Successful(
            new LoginResponse
            {
                Email = user.Email!,
                RequiresTwoFactor = result.RequiresTwoFactor,
                CurrentLocale = user.CurrentLocale
            }
        );
    }

    public async Task<ServiceResult> ValidateTwoFactorAuthForLoginAsync(TwoFactorAuthLoginRequest request)
    {
        var result =
            await signInManager.TwoFactorAuthenticatorSignInAsync(request.TwoFactorAuthToken, request.StayLoggedIn,
                false);
        if (!result.Succeeded) ServiceResult.Error(ServiceErrorType.InternalServerError, result.ToString());

        return ServiceResult.Successful();
    }

    public async Task Logout()
    {
        await signInManager.SignOutAsync();
    }

    #endregion

    //TODO email sender and test these methods

    #region emailSenderNeeded

    public async Task<ServiceResult> ForgottenPassword(string email)
    {
        var userResult = await GetByEmailAsync(email);
        if (userResult.Failed)
            return userResult;

        var user = userResult.Data;
        if (!await userManager.IsEmailConfirmedAsync(user))
            return ServiceResult.Error(ServiceErrorType.EmailNotConfirmed, "User doesn't have email confirmed");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = $"{Helper.GetEnvVar("PAGE_URL")}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendPasswordResetLinkAsync(user, email, resetLink);
        return ServiceResult.Successful();
    }

    public async Task<ServiceResult> ResetPassword(ResetPasswordRequest request)
    {
        var userResult = await GetByIdAsync(request.UserId);
        if (userResult.Failed) return userResult;

        var result = await userManager.ResetPasswordAsync(userResult.Data, request.Token, request.NewPassword);
        return result.Succeeded
            ? ServiceResult.Successful()
            : ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());
    }

    private async Task SendConfirmationEmail(UserEntity user)
    {
        if (!user.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationLink =
                $"{Helper.GetEnvVar("PAGE_URL")}/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            await emailSender.SendConfirmationLinkAsync(user, user.Email!, confirmationLink);
        }
    }

    public async Task<ServiceResult> ResendConfirmationEmail(long? userId)
    {
        if (!userId.HasValue) return ServiceResult.Error(ServiceErrorType.BadRequest, "UserId must be supplied");

        var userResult = await GetByIdAsync(userId.Value);
        if (userResult.Failed) return userResult;

        await SendConfirmationEmail(userResult.Data);
        return ServiceResult.Successful();
    }

    [AllowAnonymous]
    public async Task<ServiceResult> ConfirmEmail(long? userId, string? token)
    {
        if (!userId.HasValue || string.IsNullOrEmpty(token))
            return ServiceResult.Error(ServiceErrorType.BadRequest, "UserId and token must be supplied");

        var userResult = await GetByIdAsync(userId.Value);
        if (userResult.Failed) return userResult;

        var result = await userManager.ConfirmEmailAsync(userResult.Data, token);
        return result.Succeeded
            ? ServiceResult.Successful()
            : ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());
    }

    #endregion


    #region UserSettings

    public async Task<bool> GetTwoFactorAuthStatusAsync()
    {
        return (await GetCurrentUserAsync()).TwoFactorEnabled;
    }

    public async Task<UserResponse> GetLoggedUserDataAsync()
    {
        var loggedUser = await GetCurrentUserAsync();
        return mapper.Map<UserResponse>(loggedUser);
    }

    public async Task<ServiceResult> ChangeEmailAsync(ChangeEmailRequest request)
    {
        var user = await GetCurrentUserAsync();
        var verifyResult = await VerifyUserAsync(request, user);
        if (verifyResult.Failed)
            return verifyResult;

        //TODO Bez 2fa treba poslat email s tokenom a ten overit spravi takisto aj pri hesle
        // IdentityResult? result;
        // if (user.TwoFactorEnabled)
        // {
        //     result = await userManager.ChangeEmailAsync(user, request.NewEmail, await userManager.GenerateChangeEmailTokenAsync(user,request.NewEmail));
        // }
        // else
        // {
        //     result = await userManager.ChangeEmailAsync(user, request.NewEmail, request.TwoFactorAuthToken);
        // }
        var result = await userManager.ChangeEmailAsync(user, request.NewEmail,
            await userManager.GenerateChangeEmailTokenAsync(user, request.NewEmail));
        if (!result.Succeeded)
            return ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        return ServiceResult.Successful();
    }

    public async Task<ServiceResult<TwoFactorAuthResponse>> ToggleTwoFactorAuthAsync(VerifyUserRequest request)
    {
        var user = await GetCurrentUserAsync();
        var verifyResult = await VerifyUserAsync(request, user);
        if (verifyResult.Failed)
            return ServiceResult<TwoFactorAuthResponse>.Error(verifyResult);

        user.TwoFactorEnabled = !user.TwoFactorEnabled;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return ServiceResult<TwoFactorAuthResponse>.Error(ServiceErrorType.IdentityError,
                result.Errors.ToString());

        return await SetUpTwoFactorAuth(user);
    }

    public async Task<ServiceResult> ChangePasswordAsync(ChangePasswordRequest request)
    {
        var user = await GetCurrentUserAsync();
        var twoFactorAuthResult = await ValidateTwoFactorAuthAsync(user, request.TwoFactorAuthToken);
        if (twoFactorAuthResult.Failed) return twoFactorAuthResult;

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        return ServiceResult.Successful();
    }

    public async Task<ServiceResult> DeleteUserAccountAsync(VerifyUserRequest request)
    {
        var user = await GetCurrentUserAsync();
        var verifyResult = await VerifyUserAsync(request, user);
        if (verifyResult.Failed) return verifyResult;

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? ServiceResult.Successful()
            : ServiceResult.Error(ServiceErrorType.IdentityError, result.Errors.ToString());
    }

    public async Task<ServiceResult<string>> GenerateNewTwoFactorAuthQrCodeAsync(VerifyUserRequest request)
    {
        var user = await GetCurrentUserAsync();
        var verifyResult = await VerifyUserAsync(request, user);
        if (verifyResult.Failed) return ServiceResult<string>.Error(verifyResult);

        var qrCodeResult = await GenerateNewTwoFactorAuthQrCodeAsync(user);
        return qrCodeResult.Failed
            ? ServiceResult<string>.Error(qrCodeResult)
            : ServiceResult<string>.Successful(qrCodeResult.Data);
    }

    public async Task<ServiceResult<IEnumerable<string>>> GenerateNewTwoFactorAuthRecoveryCodesAsync(
        VerifyUserRequest request)
    {
        var user = await GetCurrentUserAsync();
        var verifyResult = await VerifyUserAsync(request, user);
        if (verifyResult.Failed)
            return ServiceResult<IEnumerable<string>>.Error(verifyResult);

        var recoveryCodesResult = await GenerateNewTwoFactorAuthRecoveryCodesAsync(user);
        return !recoveryCodesResult.Failed
            ? ServiceResult<IEnumerable<string>>.Error(recoveryCodesResult)
            : ServiceResult<IEnumerable<string>>.Successful(recoveryCodesResult.Data);
    }

    private async Task<ServiceResult> ValidateTwoFactorAuthAsync(UserEntity user, string? token)
    {
        if (!user.TwoFactorEnabled) return ServiceResult.Successful();
        if (string.IsNullOrEmpty(token))
            return ServiceResult.Error(ServiceErrorType.TwoFactorAuthRequired,
                "Two-factor authentication is required to proceed.");

        var isTokenValid = await userManager.VerifyTwoFactorTokenAsync(user,
            TokenOptions.DefaultAuthenticatorProvider, token);
        if (!isTokenValid)
            return ServiceResult.Error(ServiceErrorType.InvalidTwoFactorAuthToken,
                "Invalid two-factor authentication token.");

        return ServiceResult.Successful();
    }

    private async Task<ServiceResult> VerifyUserAsync(VerifyUserRequest request, UserEntity user)
    {
        var twoFactorAuthResult = await ValidateTwoFactorAuthAsync(user, request.TwoFactorAuthToken);
        if (twoFactorAuthResult.Failed)
            return twoFactorAuthResult;

        var isValid = await userManager.CheckPasswordAsync(user, request.Password);
        return isValid
            ? ServiceResult.Successful()
            : ServiceResult.Error(ServiceErrorType.AuthenticationFailed, "Wrong password");
    }

    #endregion

    #region Private Methods

    #region MyRegion

    private async Task<ServiceResult<TwoFactorAuthResponse>> SetUpTwoFactorAuth(UserEntity user)
    {
        if (!user.TwoFactorEnabled)
            return ServiceResult<TwoFactorAuthResponse>.Successful(
                new TwoFactorAuthResponse { TwoFactorEnabled = false });

        var qrCodeResult = await GenerateNewTwoFactorAuthQrCodeAsync(user);
        if (qrCodeResult.Failed)
            return ServiceResult<TwoFactorAuthResponse>.Error(qrCodeResult);

        var recoveryCodesResult = await GenerateNewTwoFactorAuthRecoveryCodesAsync(user);
        if (recoveryCodesResult.Failed)
            return ServiceResult<TwoFactorAuthResponse>.Error(recoveryCodesResult);

        return ServiceResult<TwoFactorAuthResponse>.Successful(
            new TwoFactorAuthResponse { TwoFactorEnabled = true, QrCode = qrCodeResult.Data, RecoveryCodes = recoveryCodesResult.Data.ToList() }
        );
    }

    private async Task<ServiceResult<string>> GenerateNewTwoFactorAuthQrCodeAsync(UserEntity user)
    {
        var result = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!result.Succeeded)
            return ServiceResult<string>.Error(ServiceErrorType.IdentityError, result.Errors.ToString());

        var totpAuthenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        return string.IsNullOrEmpty(totpAuthenticatorKey)
            ? ServiceResult<string>.Error(ServiceErrorType.NotFound, "totpAuthenticatorKey not found")
            : ServiceResult<string>.Successful(GenerateQrCode(totpAuthenticatorKey, user.Email!));
    }

    private string GenerateQrCode(string secretKey, string userEmail)
    {
        var appName = configuration.GetValue<string>("Application:Name") ??
                      throw new ArgumentNullException(nameof(configuration));
        var otpAuthUrl = $"otpauth://totp/{appName}:{userEmail}?secret={secretKey}&issuer={appName}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return Convert.ToBase64String(qrCode.GetGraphic(3));
    }

    private async Task<ServiceResult<IEnumerable<string>>> GenerateNewTwoFactorAuthRecoveryCodesAsync(UserEntity user)
    {
        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 5))?.ToList();
        return recoveryCodes is { Count: > 0 }
            ? ServiceResult<IEnumerable<string>>.Successful(recoveryCodes)
            : ServiceResult<IEnumerable<string>>.Error(ServiceErrorType.NotFound, "recoveryCodes not found");
    }

    #endregion

    private ServiceResult SetDefaultSettingsAsync(UserEntity user)
    {
        userSessionService.UserLogin(user.CurrentLocale, /*user.Timezone.ToString()*/ "");
        return ServiceResult.Successful();
    }

    private async Task<ServiceResult<UserEntity>> GetCurrentUserWithIncludes(bool withNoTracking = false, params Expression<Func<UserEntity, object?>>[] includes)
    {
        return await GetUserByIdWithIncludes(loggedUserService.GetUserId, withNoTracking, includes);
    }

    private async Task<ServiceResult<UserEntity>> GetUserByIdWithIncludes(long id, bool withNoTracking = false, params Expression<Func<UserEntity, object?>>[] includes)
    {
        return await GetUserByPropertyWithIncludes(u => u.Id == id, withNoTracking, includes);
    }

    private async Task<ServiceResult<UserEntity>> GetUserByEmailWithIncludes(string email, bool withNoTracking = false, params Expression<Func<UserEntity, object?>>[] includes)
    {
        return await GetUserByPropertyWithIncludes(u => u.Email == email, withNoTracking, includes);
    }

    private async Task<ServiceResult<UserEntity>> GetUserByPropertyWithIncludes(Expression<Func<UserEntity, bool>> getBy, bool withNoTracking = false,
        params Expression<Func<UserEntity, object?>>[] includes)
    {
        var query = includes.Aggregate(_users.AsQueryable(), (currentQuery, include) => currentQuery.Include(include));
        if (withNoTracking)
        {
            query = query.AsNoTracking();
        }

        return ProcessRepositoryResultWithEntity(await query.SingleOrErrorAsync(getBy));
    }

    private async Task<UserEntity> GetCurrentUserAsync()
    {
        var principal = loggedUserService.LoggedUserPrincipal;
        if (principal == null)
        {
            throw new NullReferenceException("Logged user principal is null");
        }
        var user = await userManager.GetUserAsync(principal);
        if (user == null) throw new UserByPrincipalNotFoundException(principal);
        return user;
    }

    private async Task<ServiceResult<UserEntity>> GetByEmailAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user == null
            ? ServiceResult<UserEntity>.Error(ServiceErrorType.NotFound, $"User with EMAIL: '{email}' was not found")
            : ServiceResult<UserEntity>.Successful(user);
    }

    private async Task<ServiceResult<UserEntity>> GetByIdAsync(long id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user == null
            ? ServiceResult<UserEntity>.Error(ServiceErrorType.NotFound, $"User with ID: '{id}' was not found")
            : ServiceResult<UserEntity>.Successful(user);
    }

    #endregion
}