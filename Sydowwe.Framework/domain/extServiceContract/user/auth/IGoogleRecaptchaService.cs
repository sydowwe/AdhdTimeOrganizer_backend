using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

public interface IGoogleRecaptchaService
{
    Task<Result> VerifyRecaptchaAsync(string token, string expectedAction);
}