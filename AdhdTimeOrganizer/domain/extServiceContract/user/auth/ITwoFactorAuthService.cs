using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface ITwoFactorAuthService
{
    Task<Result<TwoFactorAuthResponse>> SetUpTwoFactorAuth(User user);
    Task<Result> ValidateToken(User user, string? token);
    Task<Result<IEnumerable<string>>> GenerateNewRecoveryCodes(User user);
    Task<Result<string>> GenerateNewQrCode(User user);
}