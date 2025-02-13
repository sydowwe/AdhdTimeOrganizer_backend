using AdhdTimeOrganizer.Command.application.dto.request.user;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Common.domain.model.@enum;
using AdhdTimeOrganizer.Common.domain.result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

//TODO FINISH ERROR HANDLING
[Route("[controller]/[action]")]
public class UserController(IUserService userService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await userService.Logout();
        return NoContent();
    }

    [HttpPut("{locale}")]
    public async Task<IActionResult> ChangeCurrentLocale([FromRoute] AvailableLocales locale)
    {
        var result = await userService.ChangeCurrentLocaleAsync(locale);
        return HandleServiceResult(result, "Change locale");
    }

    #region NotAuthenicated

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegistrationRequest registrationRequest)
    {
        var result = await userService.RegisterAsync(registrationRequest);
        return HandleServiceResult(result, "Register");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest googleSignInRequest)
    {
        var result = await userService.GoogleSignInAsync(googleSignInRequest);
        return HandleServiceResult(result, "Google sign in");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login([FromBody] PasswordLoginRequest loginRequest)
    {
        var result = await userService.LoginAsync(loginRequest);
        return HandleServiceResult(result, "Login");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Validate2FaLogin(
        [FromBody] TwoFactorAuthLoginRequest twoFactorAuthLoginRequest)
    {
        var result = await userService.ValidateTwoFactorAuthForLoginAsync(twoFactorAuthLoginRequest);
        return HandleServiceResult(result, "2FA");
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ResendConfirmationEmail([FromQuery] long userId)
    {
        var result = await userService.ResendConfirmationEmail(userId);
        return HandleServiceResult(result, "Resend confirmation email");
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail([FromQuery] long userId, [FromQuery] string token)
    {
        var result = await userService.ConfirmEmail(userId, token);
        return HandleServiceResult(result, "Confirm email");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ForgottenPassword([FromBody] EmailRequest request)
    {
        var result = await userService.ForgottenPassword(request.Email);
        return HandleServiceResult(result, "Forgotten password");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest resetPasswordRequest)
    {
        var result = await userService.ResetPassword(resetPasswordRequest);
        return HandleServiceResult(result, "Reset password");
    }

    #endregion

    #region UserSettings


    [HttpPost]
    public async Task<IActionResult> Data()
    {
        var userResponse = await userService.GetLoggedUserDataAsync();
        return Ok(userResponse);
    }

    [HttpPost]
    public async Task<IActionResult> Two2FaStatus()
    {
        return Ok(await userService.GetTwoFactorAuthStatusAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Toggle2Fa([FromBody] VerifyUserRequest request)
    {
        var result = await userService.ToggleTwoFactorAuthAsync(request);
        return HandleServiceResult(result, "Toggle 2FA");
    }

    [HttpPost]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        var result = await userService.ChangeEmailAsync(request);
        return HandleServiceResult(result, "Change email");
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await userService.ChangePasswordAsync(request);
        return HandleServiceResult(result, "Change password");

    }

    //TODO Dokoncit error types
    [HttpPost]
    public async Task<IActionResult> DeleteAccount([FromBody] VerifyUserRequest request)
    {
        var result = await userService.DeleteUserAccountAsync(request);
        return HandleServiceResult(result, "Delete account");
    }

    [HttpPost]
    public async Task<IActionResult> Get2FaQrCode([FromBody] VerifyUserRequest request)
    {
        var result = await userService.GenerateNewTwoFactorAuthQrCodeAsync(request);
        return HandleServiceResult(result, "Get new 2FA QR code");
    }

    [HttpPost]
    public async Task<IActionResult> GenerateNew2FaRecoveryCodes([FromBody] VerifyUserRequest request)
    {
        var result = await userService.GenerateNewTwoFactorAuthRecoveryCodesAsync(request);
        return HandleServiceResult(result, "Generate new 2FA recovery codes");
    }
    #endregion


    private IActionResult HandleServiceResult(ServiceResult result, string operationContext)
    {
        if (!result.Failed)
            return NoContent();

        // var userFriendlyMessage = result.ErrorType switch
        // {
        //     ServiceErrorType.BadRequest => $"The request to {operationContext} is invalid. Please check your input and try again.",
        //     ServiceErrorType.Conflict => $"A conflict occurred while attempting to {operationContext}. Please try again.",
        //     ServiceErrorType.NotFound => $"The resource needed to {operationContext} was not found. It might have been removed or doesn't exist.",
        //     ServiceErrorType.UserLockedOut => $"Your account is currently locked. You cannot {operationContext} until it is unlocked.",
        //     ServiceErrorType.EmailNotConfirmed => $"You must confirm your email address to {operationContext}. Please check your inbox.",
        //     ServiceErrorType.TwoFactorAuthRequired => $"Two-factor authentication is required to {operationContext}. Please provide a valid code.",
        //     ServiceErrorType.AuthenticationFailed => $"Authentication failed while attempting to {operationContext}. Please check your credentials.",
        //     ServiceErrorType.IdentityError => $"An error occurred with your account while attempting to {operationContext}. Please contact support.",
        //     _ => $"An unexpected error occurred while attempting to {operationContext}. Please try again."
        // };

        return result.ErrorType switch
        {
            ServiceErrorType.BadRequest => BadRequest(new { Message = result.ErrorMessage }),
            ServiceErrorType.Conflict => Conflict(new { Message = result.ErrorMessage }),
            ServiceErrorType.NotFound => NotFound(new { Message = result.ErrorMessage }),
            ServiceErrorType.UserLockedOut => StatusCode(StatusCodes.Status423Locked, new { Message = result.ErrorMessage }),
            ServiceErrorType.EmailNotConfirmed => StatusCode(StatusCodes.Status412PreconditionFailed, new { Message = result.ErrorMessage }),
            ServiceErrorType.TwoFactorAuthRequired => Unauthorized(new { Message = result.ErrorMessage }),
            ServiceErrorType.AuthenticationFailed => Unauthorized(new { Message = result.ErrorMessage }),
            ServiceErrorType.IdentityError => StatusCode(StatusCodes.Status500InternalServerError, new { Message = "A system error occurred. Please try again later." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { Message = result.ErrorMessage })
        };
    }

    private IActionResult HandleServiceResult<T>(ServiceResult<T> result, string operationContext) where T : notnull
    {
        return result.Failed
            ? HandleServiceResult(result as ServiceResult, operationContext)
            : Ok(result.Data);
    }
}