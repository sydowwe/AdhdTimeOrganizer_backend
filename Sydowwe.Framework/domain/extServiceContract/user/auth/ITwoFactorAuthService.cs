using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

public interface ITwoFactorAuthService<TUser> where TUser : BaseUser
{
    Task<Result<TwoFactorAuthResponse>> SetUpTwoFactorAuth(TUser user);
    Task<Result> ValidateToken(TUser user, string? token);
    Task<Result<IEnumerable<string>>> GenerateNewRecoveryCodes(TUser user);
    Task<Result<string>> GenerateNewQrCode(TUser user);
    Task<Result<TUser>> ValidatePendingLoginToken(string pendingAuthToken, string totpCode, CancellationToken ct);
}