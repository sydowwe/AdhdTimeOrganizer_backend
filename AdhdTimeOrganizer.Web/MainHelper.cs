using AdhdTimeOrganizer.Common.domain.helper;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Web;

public static class MainHelper
{
    public static string GetQueryDatabaseConnectionString => GetDatabaseConnectionString("query_db");

    public static string GetCommandDatabaseConnectionString => GetDatabaseConnectionString("command_db");


    public static string GetLogDatabaseConnectionString => GetDatabaseConnectionString("log_db", Helper.GetEnvVar("LOG_DB_USER"), Helper.GetEnvVar("LOG_DB_PASSWORD"));
    private static string GetDatabaseConnectionString(string databaseName, string? username = null, string? password = null)
    {
        return
            $"Host={Helper.GetEnvVar("DB_HOST")};Port={Helper.GetEnvVar("DB_PORT")};Username={username ?? Helper.GetEnvVar("DB_USER")};Password={password ?? Helper.GetEnvVar("DB_PASSWORD")};Database={databaseName};Include Error Detail=true;Pooling=true;Timeout=300;CommandTimeout=300";
    }
    public static async Task<bool> EnsureDatabaseCreatedAsync(DbContext dbContext)
    {
        // try
        // {
        //     await dbContext.Database.EnsureCreatedAsync();
        //     Log.Information("Connected to database");
        //     return true;
        // }
        // catch (System.Exception ex)
        // {
        //     Log.Fatal(ex, "Could not connect to the database.");
        //     return false;
        // }
        return true;
    }
}