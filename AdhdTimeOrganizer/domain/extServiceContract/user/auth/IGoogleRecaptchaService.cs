using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IGoogleRecaptchaService
{
    Task<Result> VerifyRecaptchaAsync(string token, string expectedAction);
}