using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class TwoFactorAuthService(UserManager<User> userManager, IConfiguration configuration) : ITwoFactorAuthService, IScopedService
{
    public async Task<Result<TwoFactorAuthResponse>> SetUpTwoFactorAuth(User user)
    {
        if (!user.TwoFactorEnabled)
            return Result<TwoFactorAuthResponse>.Successful(
                new TwoFactorAuthResponse { TwoFactorEnabled = false });

        var qrCodeResult = await GenerateNewQrCode(user);
        if (qrCodeResult.Failed)
            return qrCodeResult.ToFailed<TwoFactorAuthResponse>();

        var recoveryCodesResult = await GenerateNewRecoveryCodes(user);
        if (recoveryCodesResult.Failed)
            return recoveryCodesResult.ToFailed<TwoFactorAuthResponse>();

        return Result<TwoFactorAuthResponse>.Successful(
            new TwoFactorAuthResponse { TwoFactorEnabled = true, QrCode = qrCodeResult.Data, RecoveryCodes = recoveryCodesResult.Data.ToList() }
        );
    }

    public async Task<Result> ValidateToken(User user, string? token)
    {
        if (!user.TwoFactorEnabled) return Result.Successful();
        if (string.IsNullOrEmpty(token))
            return Result.Error(ResultErrorType.TwoFactorAuthRequired,
                "Two-factor authentication is required to proceed.");

        var isTokenValid = await userManager.VerifyTwoFactorTokenAsync(user,
            TokenOptions.DefaultAuthenticatorProvider, token);
        if (!isTokenValid)
            return Result.Error(ResultErrorType.InvalidTwoFactorAuthToken,
                "Invalid two-factor authentication token.");

        return Result.Successful();
    }

    public async Task<Result<IEnumerable<string>>> GenerateNewRecoveryCodes(User user)
    {
        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 5))?.ToList();
        return recoveryCodes is { Count: > 0 }
            ? Result<IEnumerable<string>>.Successful(recoveryCodes)
            : Result<IEnumerable<string>>.Error(ResultErrorType.NotFound, "recoveryCodes not found");
    }

    public async Task<Result<string>> GenerateNewQrCode(User user)
    {
        var result = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!result.Succeeded)
            return Result<string>.Error(ResultErrorType.IdentityError, result.Errors.ToString());

        var totpAuthenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        return string.IsNullOrEmpty(totpAuthenticatorKey)
            ? Result<string>.Error(ResultErrorType.NotFound, "totpAuthenticatorKey not found")
            : Result<string>.Successful(GenerateQrCode(totpAuthenticatorKey, user.Email!));
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
}