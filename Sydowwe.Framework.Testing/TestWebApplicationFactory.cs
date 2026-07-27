using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// Portal-agnostic test host factory. <c>TProgram</c> is the portal's entry point (its global <c>Program</c>).
/// <para>Pass <c>roles</c> = the role claims the test user should have (e.g. <c>["Admin","User"]</c>),
/// or <c>null</c> to register no auth handler at all â€” requests then hit the real auth layer and
/// protected endpoints return 401 (unauthenticated).</para>
/// <para>Pass <c>userId</c> to log the test in as a non-default user (useful for cross-user IDOR / ownership tests).</para>
/// </summary>
public class TestWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>, ITestClientFactory
    where TProgram : class
{
    private readonly string _connectionString;
    private readonly string[]? _roles;
    private readonly long _userId;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IEnumerable<KeyValuePair<string, string?>>? _configOverrides;
    private readonly bool _preserveExecutionContext;

    public TestWebApplicationFactory(string connectionString, string[]? roles, long? userId = null,
        Action<IServiceCollection>? configureServices = null,
        IEnumerable<KeyValuePair<string, string?>>? configOverrides = null,
        bool preserveExecutionContext = false)
    {
        _connectionString = connectionString;
        _roles = roles;
        _userId = userId ?? FakeLoggedUserService.TestUserId;
        _configureServices = configureServices;
        _configOverrides = configOverrides;
        _preserveExecutionContext = preserveExecutionContext;
        SetEnvVars(connectionString);
    }

    private static void SetEnvVars(string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        Environment.SetEnvironmentVariable("DB_HOST", csb.Host ?? "localhost");
        Environment.SetEnvironmentVariable("DB_PORT", (csb.Port == 0 ? 5432 : csb.Port).ToString());
        Environment.SetEnvironmentVariable("DB_USER", csb.Username ?? "test");
        Environment.SetEnvironmentVariable("DB_PASSWORD", csb.Password ?? "test");
        Environment.SetEnvironmentVariable("DB_NAME", csb.Database ?? "hb_tests");
        Environment.SetEnvironmentVariable("PAGE_URL", "https://localhost:3333");
        Environment.SetEnvironmentVariable("API_URL", "https://localhost:5555");
        Environment.SetEnvironmentVariable("ROOT_ADMIN_USERNAME", "hbcleaning");
        Environment.SetEnvironmentVariable("ROOT_ADMIN_PASSWORD", "hbcleaning");
        Environment.SetEnvironmentVariable("ROOT_ADMIN_ENTRAID_ID", "test-entraid-id");
        Environment.SetEnvironmentVariable("COMPANY_DOMAIN", "hbcleaning.sk");
        Environment.SetEnvironmentVariable("RECAPTCHA_SECRET", "test-recaptcha-secret");
        Environment.SetEnvironmentVariable("LOG_DB_USER", "test");
        Environment.SetEnvironmentVariable("LOG_DB_PASSWORD", "test");
        Environment.SetEnvironmentVariable("ENTRAID_TENANT_ID", "test-tenant-id");
        Environment.SetEnvironmentVariable("ENTRAID_CLIENT_ID", "test-client-id");
        Environment.SetEnvironmentVariable("ENTRAID_CLIENT_SECRET", "test-client-secret");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "test-issuer");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "test-audience");
        Environment.SetEnvironmentVariable("ECDSA_PRIVATE_KEY_PATH", "secrets/ec_private.pem");
        Environment.SetEnvironmentVariable("FIELD_ENCRYPTION_KEY", "0AnY3W6P7P07Z9qjntQRQYvEVR4XLaa2OYs7R0jW4aw=");
    }

    // The in-process TestServer suppresses ExecutionContext flow from the caller into the request pipeline
    // by default (PreserveExecutionContext = false), so an AsyncLocal a test sets before issuing a request
    // (e.g. BusinessClock.UseTimeProvider pinning a fixed instant) never reaches the handler. Opt-in per
    // factory: clock/date tests pass preserveExecutionContext: true so those ambient overrides flow through,
    // without changing the behaviour of the shared cached factories every other test uses.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        if (_preserveExecutionContext)
            ((TestServer)host.Services.GetRequiredService<IServer>()).PreserveExecutionContext = true;
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Highest-precedence config source: applied after the host's own appsettings.json, so a test can
        // pin a flag (e.g. an endpoint's feature toggle) regardless of what the copied appsettings holds.
        if (_configOverrides is not null)
            builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(_configOverrides));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            // Register via a factory delegate (NOT a pre-built instance): the DI container only disposes
            // IDisposable singletons it creates itself. A pre-built instance passed to AddSingleton(instance)
            // is never disposed when the host is torn down, so every transient factory from CreateFactory(...)
            // would leak its entire NpgsqlDataSource connection pool until GC — exhausting the Postgres
            // container's max_connections (53300: "too many clients already"). With the factory overload the
            // container owns the data source and disposes it when the factory/host is disposed.
            services.AddSingleton<NpgsqlDataSource>(_ =>
            {
                var builder = new NpgsqlDataSourceBuilder(_connectionString) { ConnectionStringBuilder = { MaxPoolSize = 20 } };
                return builder.EnableDynamicJson().Build();
            });

            // The TestServer never sends an X-Forwarded-For header, but the auth endpoints throttle on
            // it (FastEndpoints rejects a request lacking the throttle header with 403 "Forbidden by
            // rate limiting middleware!" before the handler runs). This filter stamps a UNIQUE value
            // per request at the front of the pipeline, so the throttle treats every test request as a
            // distinct client and never interferes with functional assertions. Tests that deliberately
            // exercise throttling send their own X-Forwarded-For; the filter only fills it in when absent.
            services.AddSingleton<IStartupFilter, ThrottleHeaderStartupFilter>();

            services.AddScoped<ILoggedUserService>(_ => new FakeLoggedUserService(_userId));

            // _roles == null => unauthenticated mode: leave the real auth layer in place.
            if (_roles is not null)
            {
                var roles = _roles;
                var userId = _userId;
                services.AddAuthentication(o =>
                    {
                        o.DefaultAuthenticateScheme = RoleTestAuthHandler.SchemeName;
                        o.DefaultChallengeScheme = RoleTestAuthHandler.SchemeName;
                        o.DefaultScheme = RoleTestAuthHandler.SchemeName;
                    })
                    .AddScheme<RoleTestAuthHandlerOptions, RoleTestAuthHandler>(
                        RoleTestAuthHandler.SchemeName, o =>
                        {
                            o.Roles = roles;
                            o.UserId = userId;
                        });
            }

            // Per-test service overrides run last so they win over the host's registrations
            // (e.g. swapping IWordTemplateService or a SharePoint command handler for a fake).
            _configureServices?.Invoke(services);
        });
    }

    // The production pipeline keys the FastEndpoints throttle on the validated client IP
    // (UseClientIpThrottleKey, after UseForwardedHeaders), but the TestServer leaves
    // Connection.RemoteIpAddress null, so the throttle would have no key and reject every request with
    // 403. This filter assigns a client IP at the front of the pipeline: a constant one taken from a
    // test-supplied X-Forwarded-For (so throttle tests can simulate one client), otherwise a UNIQUE IP
    // per request (so ordinary functional tests never trip the throttle).
    private sealed class ThrottleHeaderStartupFilter : IStartupFilter
    {
        private static int _requestCounter;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
                    context.Connection.RemoteIpAddress =
                        !string.IsNullOrEmpty(forwardedFor) && IPAddress.TryParse(forwardedFor.Split(',')[0].Trim(), out var supplied)
                            ? supplied
                            : NextUniqueIp();

                    await nextMiddleware();
                });
                next(app);
            };
        }

        private static IPAddress NextUniqueIp()
        {
            var n = Interlocked.Increment(ref _requestCounter);
            return new IPAddress([10, (byte)(n >> 16), (byte)(n >> 8), (byte)n]);
        }
    }
}