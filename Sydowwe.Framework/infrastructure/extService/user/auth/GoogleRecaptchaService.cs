using System.Diagnostics.CodeAnalysis;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

public class GoogleRecaptchaService(HttpClient httpClient) : IGoogleRecaptchaService
{
    private const string RecaptchaApiUrl = "https://www.google.com/recaptcha/api/siteverify";

    private static float ScoreThreshold =>
        float.TryParse(Environment.GetEnvironmentVariable("RECAPTCHA_SCORE_THRESHOLD"), out var t) ? t : 0.7f;

    public async Task<Result> VerifyRecaptchaAsync(string token, string expectedAction)
    {
        var formContent = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("secret", Helper.GetEnvVar("RECAPTCHA_SECRET")),
            new KeyValuePair<string, string>("response", token)
        ]);
        var response = await httpClient.PostAsync(RecaptchaApiUrl, formContent);
        if (!response.IsSuccessStatusCode)
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "reCAPTCHA verification request failed");

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonHelper.Deserialize<RecaptchaResponse>(json);
        if (result is null)
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "The verify recaptcha call failed because the token was invalid");

        if (!result.success)
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "reCAPTCHA verification failed");

        if (!expectedAction.Equals(result.action))
            return Result.Error(ResultErrorType.RecaptchaWrongAction,
                $"The action attribute in reCAPTCHA tag is: {result.action}, expected action was {expectedAction}");

        if (result.score < ScoreThreshold)
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "Score too low");

        return Result.Successful();
    }
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class RecaptchaResponse
{
    public bool success { get; init; }
    public float score { get; init; }
    public string? action { get; init; }
    public string? challengeTs { get; init; }
    public string? hostname { get; init; }
    public string[]? errorCodes { get; init; }
}