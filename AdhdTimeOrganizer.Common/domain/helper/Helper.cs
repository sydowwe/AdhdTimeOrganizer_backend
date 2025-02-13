using System.Text.RegularExpressions;
using AdhdTimeOrganizer.Common.domain.exception;

namespace AdhdTimeOrganizer.Common.domain.helper;

public static partial class Helper
{
    public static string GetEnvVar(string envName)
    {
        return Environment.GetEnvironmentVariable(envName) ?? throw new EnvironmentVariableMissingException(envName);
    }

    public static Uri GetApiUri()
    {
        return new Uri(GetEnvVar("API_URL"));
    }


    public static string GetAppLogoUrl()
    {
        return Path.Combine(GetApiUri().ToString(), "images", "logo.png");
    }
    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SlugifyRegex();

    public static string FromPascalCaseToKebabCase(string value)
    {
        return SlugifyRegex().Replace(value!, "$1-$2").ToLower();
    }

    public static string FromPascalCaseToSnakeCase(string value)
    {
        return SlugifyRegex().Replace(value, "$1_$2").ToLower();
    }
}