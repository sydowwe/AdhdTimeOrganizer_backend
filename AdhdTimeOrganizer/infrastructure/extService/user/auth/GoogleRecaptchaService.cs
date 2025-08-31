using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

//TODO use the google cloud way
public class GoogleRecaptchaService(HttpClient httpClient) : IGoogleRecaptchaService
{
    private const string RecaptchaApiUrl = "https://www.google.com/recaptcha/api/siteverify";

    // private const string ProjectId = "time-tracker-app-411119";
    // private static RecaptchaEnterpriseServiceClient? _client;
    // private static readonly Lock Lock = new();

    public async Task<Result> VerifyRecaptchaAsync(string token, string expectedAction)
    {
        //TODO fix recaptcha
        var response =
            await httpClient.PostAsync(
                $"{RecaptchaApiUrl}?secret={Helper.GetEnvVar("RECAPTCHA_SECRET")}&response={token}", null);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RecaptchaResponse>(json);
        if (result is null)
        {
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "The verify recaptcha call failed because the token was invalid");
        }

        if (!expectedAction.Equals(result.action))
        {
            return Result.Error(ResultErrorType.RecaptchaWrongAction,
                $"The action attribute in reCAPTCHA tag is: {result.action}, expected action was {expectedAction}");
        }

        if (result.score < 0.7)
        {
            return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
                "Score too low");
        }

        return Result.Successful();
    }
    // public async Task<Result> VerifyRecaptchaAsync(string token, string expectedAction)
    // {
    //     if (_client == null)
    //     {
    //         lock (Lock)
    //         {
    //             _client ??= RecaptchaEnterpriseServiceClient.Create();
    //         }
    //     }
    //
    //     var projectName = new ProjectName(ProjectId);
    //
    //     var createAssessmentRequest = new CreateAssessmentRequest
    //     {
    //         Assessment = new Assessment
    //         {
    //             Event = new Event
    //             {
    //                 SiteKey = Helper.GetEnvVar("RECAPTCHA_SECRET"),
    //                 Token = token,
    //                 ExpectedAction = expectedAction
    //             },
    //         },
    //         ParentAsProjectName = projectName
    //     };
    //
    //     var response = await _client.CreateAssessmentAsync(createAssessmentRequest);
    //
    //     if (response.TokenProperties.Valid == false)
    //     {
    //         return Result.Error(ResultErrorType.RecaptchaTokenInvalid,
    //             "The verify recaptcha call failed because the token was: " + response.TokenProperties.InvalidReason);
    //     }
    //
    //     // Check if the expected action was executed.
    //     if (response.TokenProperties.Action != expectedAction)
    //     {
    //         return Result.Error(ResultErrorType.RecaptchaWrongAction,
    //             $"The action attribute in reCAPTCHA tag is: {response.TokenProperties.Action}, expected action was {expectedAction}");
    //     }
    //
    //     // Get the risk score and the reason(s).
    //     // For more information on interpreting the assessment, see:
    //     // https://cloud.google.com/recaptcha-enterprise/docs/interpret-assessment
    //     // System.Console.WriteLine("The reCAPTCHA score is: " + ((decimal)response.RiskAnalysis.Score));
    //     //
    //     // foreach (RiskAnalysis.Types.ClassificationReason reason in response.RiskAnalysis.Reasons)
    //     // {
    //     //     System.Console.WriteLine(reason.ToString());
    //     // }
    //     return Result.Successful();
    // }
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class RecaptchaResponse
{
    public bool success { get; init; }
    public float score { get; init; }
    public string action { get; init; }
    public string? challengeTs { get; init; }
    public string hostname { get; init; }
    public string[]? errorCodes { get; init; }
}