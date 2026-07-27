using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Sydowwe.Framework.domain.exception;

namespace Sydowwe.Framework.domain.helper;

public static partial class Helper
{
    public static string GetEnvVar(string envName) => Environment.GetEnvironmentVariable(envName) ?? throw new EnvironmentVariableMissingException(envName);

    public static string GetConfigVar(IConfiguration configuration, string name) => configuration.GetValue<string>(name) ?? throw new ConfigurationVariableMissingException(name);

    public static Uri GetApiUri() => new(GetEnvVar("API_URL"));

    public static Uri GetPageUri() => new(GetEnvVar("PAGE_URL"));


    public static string ConvertToBase64(byte[] data, string mimeType)
    {
        var base64String = Convert.ToBase64String(data);
        return $"data:{mimeType};base64,{base64String}";
    }

    public static string ConvertPdfToBase64(byte[] data) => ConvertToBase64(data, "application/pdf");

    public static string GetAppLogoUrl() => Path.Combine(GetApiUri().ToString(), "images", "logo.png");

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SlugifyRegex();

    public static string FromPascalCaseToKebabCase(string value) => SlugifyRegex().Replace(value, "$1-$2").ToLower();

    public static string FromPascalCaseToSnakeCase(string value) => SlugifyRegex().Replace(value, "$1_$2").ToLower();

    public static string GetDatabaseConnectionString(string? databaseName = null, string? username = null, string? password = null, string? host = null, string? port = null) =>
        $"Host={host ?? GetEnvVar("DB_HOST")};Port={port ?? GetEnvVar("DB_PORT")};Username={username ?? GetEnvVar("DB_USER")};Password={password ?? GetEnvVar("DB_PASSWORD")};Database={databaseName ?? GetEnvVar("DB_NAME")};Include Error Detail=true;Pooling=true;Timeout=300;CommandTimeout=300";

    public static TimeZoneInfo GetTimezone(IConfiguration configuration)
    {
        var timezoneId = GetConfigVar(configuration, "Application:Timezone");
        return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    }

    public static double GetTimezoneUtcOffsetHours(IConfiguration configuration)
    {
        var timeZoneInfo = GetTimezone(configuration);
        return timeZoneInfo.BaseUtcOffset.TotalHours;
    }
}