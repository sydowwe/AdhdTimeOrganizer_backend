using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.config;

public static class DatabaseStringsHelper
{
    // Helper.GetDatabaseConnectionString hardcodes "Include Error Detail=true", which makes Npgsql
    // embed parameter values (potentially PII) in exception messages. Npgsql connection strings take
    // the last occurrence of a duplicate key, so appending an override here disables it in Production
    // without needing a submodule change.
    private static readonly bool IsProduction = string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

    public static string GetDefaultDatabaseConnectionString => WithErrorDetailOverride(Helper.GetDatabaseConnectionString());

    public static string GetLogDatabaseConnectionString => WithErrorDetailOverride(Helper.GetDatabaseConnectionString("log_db", Helper.GetEnvVar("LOG_DB_USER"), Helper.GetEnvVar("LOG_DB_PASSWORD")));

    private static string WithErrorDetailOverride(string connectionString) =>
        IsProduction ? $"{connectionString};Include Error Detail=false" : connectionString;
}