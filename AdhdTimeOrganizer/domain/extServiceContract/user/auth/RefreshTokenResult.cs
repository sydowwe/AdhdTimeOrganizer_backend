namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public record RefreshTokenResult
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public bool IsStayLoggedIn { get; init; }
    public string? Error { get; init; }

    public static RefreshTokenResult Ok(string accessToken, string refreshToken, bool isStayLoggedIn) => new()
    {
        Success = true,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        IsStayLoggedIn = isStayLoggedIn
    };

    public static RefreshTokenResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
