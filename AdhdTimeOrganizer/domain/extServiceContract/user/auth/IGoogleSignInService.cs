using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.extService.user.auth;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IGoogleSignInService
{
    Task<Result<GoogleUserInfo>> GetUserInfoFromGoogleSignInCode(string code);
}
