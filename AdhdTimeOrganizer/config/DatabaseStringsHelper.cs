using AdhdTimeOrganizer.domain.helper;

namespace AdhdTimeOrganizer.config;

public static class DatabaseStringsHelper
{
    public static string GetDefaultDatabaseConnectionString => Helper.GetDatabaseConnectionString();


    public static string GetLogDatabaseConnectionString => Helper.GetDatabaseConnectionString("log_db", Helper.GetEnvVar("LOG_DB_USER"), Helper.GetEnvVar("LOG_DB_PASSWORD"));
}