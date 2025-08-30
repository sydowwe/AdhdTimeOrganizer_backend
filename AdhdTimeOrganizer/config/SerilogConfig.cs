using NpgsqlTypes;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PostgreSQL;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

namespace AdhdTimeOrganizer.config;

public static class SerilogConfig
{
    public static void ConfigureSerilog(IConfiguration configuration, IHostBuilder hostBuilder, string databaseConnectionString)
    {
        hostBuilder.UseSerilog((context, config) =>
        {
            config.MinimumLevel.Is(LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithCorrelationId()
                .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message}{NewLine}{Exception}")
                .WriteTo.File("logs/log-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message}{NewLine}{Exception}");

            var columnOptions = new Dictionary<string, ColumnWriterBase>
            {
                { "timestamp", new TimestampColumnWriter(NpgsqlDbType.TimestampTz) },
                { "level", new LevelColumnWriter(true, NpgsqlDbType.Varchar) },
                { "message", new RenderedMessageColumnWriter(NpgsqlDbType.Text) },
                { "exception", new ExceptionColumnWriter(NpgsqlDbType.Text) },
                { "properties", new PropertiesColumnWriter(NpgsqlDbType.Jsonb) },

                { "server_name", new SinglePropertyColumnWriter("MachineName", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                { "logger", new SinglePropertyColumnWriter("SourceContext", PropertyWriteMethod.Raw) },

                { "correlation_id", new SinglePropertyColumnWriter("CorrelationId", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                { "event_id", new SinglePropertyColumnWriter("EventId:Id", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) },
                { "event_name", new SinglePropertyColumnWriter("EventId:Name", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                { "trace_id", new SinglePropertyColumnWriter("TraceId", PropertyWriteMethod.Raw, NpgsqlDbType.Uuid) },

                { "span_id", new SinglePropertyColumnWriter("SpanId", PropertyWriteMethod.Raw, NpgsqlDbType.Uuid) },
                { "session_id", new SinglePropertyColumnWriter("SessionId", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },

                { "request_path", new SinglePropertyColumnWriter("RequestPath", PropertyWriteMethod.Raw) },
                { "request_method", new SinglePropertyColumnWriter("RequestMethod", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                { "response_status", new SinglePropertyColumnWriter("ResponseStatus", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) },
                { "response_time_ms", new SinglePropertyColumnWriter("ResponseTimeMs", PropertyWriteMethod.Raw, NpgsqlDbType.Double) },

                // { "user_agent", new SinglePropertyColumnWriter("UserAgent", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                // { "client_ip", new SinglePropertyColumnWriter("ClientIp", PropertyWriteMethod.Raw, NpgsqlDbType.Inet) },

                // { "auth_method", new SinglePropertyColumnWriter("AuthenticationMethod", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) },
                // { "user_id", new SinglePropertyColumnWriter("UserId", PropertyWriteMethod.Raw, NpgsqlDbType.Bigint) },
                // { "role", new SinglePropertyColumnWriter("UserRole", PropertyWriteMethod.Raw, NpgsqlDbType.Varchar) }
            };

            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
            {
                config.WriteTo.PostgreSQL(
                    databaseConnectionString,
                    "warning_logs",
                    columnOptions,
                    LogEventLevel.Information,
                    null,
                    null,
                    30,
                    int.MaxValue,
                    null,
                    false,
                    "command",
                    true,
                    true,
                    e => Log.Error(e.Message),
                    configuration
                );
            }
        });
    }
}