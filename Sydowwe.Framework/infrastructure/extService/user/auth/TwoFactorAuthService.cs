using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using QRCoder;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

public class TwoFactorAuthService<TUser>(
    UserManager<TUser> userManager,
    IConfiguration configuration,
    IJwtService jwtService,
    IDistributedCache cache)
    : ITwoFactorAuthService<TUser>
    where TUser : BaseUser
{
    private const int RecoveryCodeCount = 5;

    /// <summary>Must match the partial-auth token lifetime in <c>JwtService</c>.</summary>
    private static readonly TimeSpan TwoFactorPendingLifetime = TimeSpan.FromMinutes(10);

    public async Task<Result<TwoFactorAuthResponse>> SetUpTwoFactorAuth(TUser user)
    {
        if (!user.TwoFactorEnabled)
            return Result<TwoFactorAuthResponse>.Successful(
                new TwoFactorAuthResponse { TwoFactorEnabled = false });

        if (string.IsNullOrEmpty(user.Email))
            return Result<TwoFactorAuthResponse>.Error(ResultErrorType.MissingArgument,
                "User has no email address to bind the authenticator to.");

        var existingKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (existingKey != null)
        {
            // 2FA is already provisioned: only re-render the QR for the existing key.
            // Do NOT regenerate recovery codes here — that would silently invalidate the
            // codes the user already saved. Regeneration is an explicit action via
            // GenerateNewRecoveryCodes.
            var existingQrResult = GenerateQrCode(existingKey, user.Email);
            return existingQrResult.Failed
                ? existingQrResult.ToFailed<TwoFactorAuthResponse>()
                : Result<TwoFactorAuthResponse>.Successful(
                    new TwoFactorAuthResponse { TwoFactorEnabled = true, QrCode = existingQrResult.Data });
        }

        // First-time setup: provision a fresh key and the initial recovery codes together.
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

    public async Task<Result> ValidateToken(TUser user, string? token)
    {
        if (!user.TwoFactorEnabled)
            return Result.Successful();
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

    public async Task<Result<IEnumerable<string>>> GenerateNewRecoveryCodes(TUser user)
    {
        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList();
        return recoveryCodes is { Count: > 0 }
            ? Result<IEnumerable<string>>.Successful(recoveryCodes)
            : Result<IEnumerable<string>>.Error(ResultErrorType.IdentityError, "Failed to generate recovery codes.");
    }

    public async Task<Result<string>> GenerateNewQrCode(TUser user)
    {
        if (string.IsNullOrEmpty(user.Email))
            return Result<string>.Error(ResultErrorType.MissingArgument,
                "User has no email address to bind the authenticator to.");

        var result = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!result.Succeeded)
            return Result<string>.Error(ResultErrorType.IdentityError, string.Join(", ", result.Errors.Select(e => e.Description)));

        var totpAuthenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        return string.IsNullOrEmpty(totpAuthenticatorKey)
            ? Result<string>.Error(ResultErrorType.IdentityError, "Authenticator key could not be generated.")
            : GenerateQrCode(totpAuthenticatorKey, user.Email);
    }

    /// <summary>
    /// The single validation path for the 2FA step of login, whatever transport carried the pending
    /// token — browser cookie, extension/desktop request body, anything else.
    ///
    /// <para>The token itself is the signed partial-auth JWT minted by
    /// <see cref="IJwtService.IssueTwoFactorPendingToken"/>; this method adds the three guards a bare
    /// signature check doesn't give you: <b>single use</b> (the <c>jti</c> is marked consumed before
    /// the code is even checked, so one password step buys exactly one attempt), the <b>lockout</b>
    /// check, and feeding <c>AccessFailedAsync</c> on a wrong code so repeated attempts actually lock
    /// the account.</para>
    ///
    /// <para>Accepts a TOTP code <i>or</i> a single-use recovery code — a user who has lost their
    /// authenticator has no other way back in, since this step precedes any session.</para>
    ///
    /// <para><b>Deployment note:</b> the single-use guard is only as distributed as
    /// <see cref="IDistributedCache"/>. With the in-memory default it is per-process, so a
    /// multi-instance deployment needs a shared cache (e.g. Redis) for the guard to hold.</para>
    /// </summary>
    public async Task<Result<TUser>> ValidatePendingLoginToken(string pendingAuthToken, string totpCode, CancellationToken ct)
    {
        var pending = jwtService.ReadTwoFactorPendingToken(pendingAuthToken);
        if (pending is null)
            return Result<TUser>.Error(ResultErrorType.Unauthorized, "Invalid or expired authentication session");

        var cacheKey = $"2fa-consumed:{pending.TokenId}";
        if (await cache.GetStringAsync(cacheKey, ct) is not null)
            return Result<TUser>.Error(ResultErrorType.Unauthorized, "Invalid or expired authentication session");

        var user = await userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
            return Result<TUser>.Error(ResultErrorType.Unauthorized, "Invalid or expired authentication session");

        // Consume before validating — even a wrong code costs the whole token and requires a fresh login.
        // The entry only has to outlive the token, hence the pending-token lifetime rather than a guess.
        await cache.SetStringAsync(cacheKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TwoFactorPendingLifetime
        }, ct);

        if (await userManager.IsLockedOutAsync(user))
            return Result<TUser>.Error(ResultErrorType.UserLockedOut, "Account is temporarily locked. Please try again later.");

        // A user whose authenticator is gone can still get in with a recovery code; redemption is
        // single-use and handled by Identity.
        var authenticated = !(await ValidateToken(user, totpCode)).Failed ||
                            (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, totpCode)).Succeeded;

        if (!authenticated)
        {
            await userManager.AccessFailedAsync(user);
            return Result<TUser>.Error(ResultErrorType.InvalidTwoFactorAuthToken, "Invalid two-factor authentication token.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return Result<TUser>.Successful(user);
    }

    private Result<string> GenerateQrCode(string secretKey, string userEmail)
    {
        var appName = configuration.GetValue<string>("Application:Name");
        if (string.IsNullOrEmpty(appName))
            return Result<string>.Error(ResultErrorType.InternalServerError,
                "Application:Name is not configured.");

        var otpAuthUrl =
            $"otpauth://totp/{Uri.EscapeDataString(appName)}:{Uri.EscapeDataString(userEmail)}" +
            $"?secret={secretKey}&issuer={Uri.EscapeDataString(appName)}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return Result<string>.Successful(Convert.ToBase64String(qrCode.GetGraphic(3)));
    }
}