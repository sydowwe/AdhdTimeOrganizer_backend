using AdhdTimeOrganizer.infrastructure.extService.user.auth;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IGoogleSignInService
{
    Task<Result<GoogleUserInfo>> GetUserInfoFromGoogleSignInCode(string code);
}