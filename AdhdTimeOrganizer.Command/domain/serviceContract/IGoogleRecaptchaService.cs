using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.domain.serviceContract;

public interface IGoogleRecaptchaService
{
    Task<ServiceResult> VerifyRecaptchaAsync(string token, string expectedAction);
}