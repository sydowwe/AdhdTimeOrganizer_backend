using AdhdTimeOrganizer.Command.application.dto.request.user;
using AdhdTimeOrganizer.Command.application.dto.response.user;
using AdhdTimeOrganizer.Common.domain.model.@enum;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.application.@interface.users;

public interface IUserService
{
    Task<ServiceResult<TwoFactorAuthResponse>> PasswordRegisterAsync(PasswordRegistrationRequest request);
    Task<ServiceResult<GoogleSignInResponse>> GoogleSignInAsync(GoogleSignInRequest googleSignInRequest);
    Task<ServiceResult<LoginResponse>> LoginAsync(PasswordLoginRequest loginRequest);
    Task<ServiceResult> ValidateTwoFactorAuthForLoginAsync(TwoFactorAuthLoginRequest request);
    Task Logout();
    Task<ServiceResult> ResendConfirmationEmail(long? userId);
    Task<ServiceResult> ConfirmEmail(long? userId, string? token);
    Task<ServiceResult> ForgottenPassword(string email);
    Task<ServiceResult> ResetPassword(ResetPasswordRequest request);
    Task<ServiceResult> ChangeEmailAsync(ChangeEmailRequest request);
    Task<ServiceResult> ChangePasswordAsync(ChangePasswordRequest request);
    Task<ServiceResult<TwoFactorAuthResponse>> ToggleTwoFactorAuthAsync(VerifyUserRequest request);
    Task<ServiceResult> DeleteUserAccountAsync(VerifyUserRequest request);
    Task<ServiceResult> ChangeCurrentLocaleAsync(AvailableLocales locale);
    Task<bool> GetTwoFactorAuthStatusAsync();
    Task<UserResponse> GetLoggedUserDataAsync();
    Task<ServiceResult<string>> GenerateNewTwoFactorAuthQrCodeAsync(VerifyUserRequest request);
    Task<ServiceResult<IEnumerable<string>>> GenerateNewTwoFactorAuthRecoveryCodesAsync(VerifyUserRequest request);
}