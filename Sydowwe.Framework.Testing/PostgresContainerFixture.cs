using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Respawn;
using Sydowwe.Framework.domain.extServiceContract.user;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// Portal-agnostic Postgres (Testcontainers) collection fixture. A portal supplies a closed subclass —
/// <c>class XFixture : PostgresContainerFixture&lt;Program, XDbContext&gt;</c> — plus a
/// <c>[CollectionDefinition(“Postgres”)] class ... : ICollectionFixture&lt;XFixture&gt;</c>, and overrides
/// the hooks below for any schema/seed specifics (materialized views, the test user, etc.).
/// </summary>
public abstract class PostgresContainerFixture<TProgram, TDbContext> : IPostgresFixture, IAsyncLifetime
    where TProgram : class
    where TDbContext : DbContext
{
    static PostgresContainerFixture()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        // Encryption key for at-rest PII columns — needed when this fixture builds the DbContext
        // (model building runs the value converter) before any TestWebApplicationFactory is created.
        Environment.SetEnvironmentVariable("FIELD_ENCRYPTION_KEY", "0AnY3W6P7P07Z9qjntQRQYvEVR4XLaa2OYs7R0jW4aw=");
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("database")
        .WithUsername("test")
        .WithPassword("test")
        .WithPortBinding(5432, 5432)
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Include Error Detail=true";

    // Cached factories for the common role combinations. Cross-user / custom-userId tests should
    // call CreateFactory(roles, userId) to build a fresh one and dispose it themselves.
    public ITestClientFactory AdminAndUserFactory { get; private set; } = null!;
    public ITestClientFactory AdminFactory { get; private set; } = null!;
    public ITestClientFactory UserFactory { get; private set; } = null!;
    public ITestClientFactory RootFactory { get; private set; } = null!;
    public ITestClientFactory UnauthenticatedFactory { get; private set; } = null!;

    private NpgsqlDataSource? _dataSource;

    // The long-lived primary host's FastEndpoints command-bus resolver. Captured once the primary host is
    // built; transient hosts from CreateFactory re-pin it on dispose so the process-global resolver never
    // dangles at a torn-down host. See FastEndpointsCommandBus for the full rationale.
    private object? _primaryResolver;

    // ---- portal hooks -------------------------------------------------------------------------

    /// <summary>Construct the portal's concrete DbContext (the only thing the generic can't do itself).</summary>
    protected abstract TDbContext NewDbContext(DbContextOptions<TDbContext> options, ILoggedUserService user);

    /// <summary>Runs once after <c>EnsureCreated</c> — create materialized views / objects EF skips. Default: nothing.</summary>
    protected virtual Task OnSchemaCreatedAsync(TDbContext db) => Task.CompletedTask;

    /// <summary>Runs after initial create AND after every Respawn reset — seed the baseline (test user, etc.). Default: nothing.</summary>
    protected virtual Task SeedFixtureAsync(TDbContext db) => Task.CompletedTask;

    /// <summary>Runs after every Respawn reset (e.g. REFRESH MATERIALIZED VIEW). Default: nothing.</summary>
    protected virtual Task AfterResetAsync(TDbContext db) => Task.CompletedTask;

    /// <summary>
    /// Portal-wide service overrides applied to every factory this fixture builds — the cached role
    /// factories AND ad-hoc ones from <see cref="CreateFactory"/> (composed before that call's own
    /// <c>configureServices</c>, which still wins on conflicts). Use for swaps that must hold for every
    /// test regardless of client (e.g. mocking an outbound recaptcha/email service). Default: nothing.
    /// </summary>
    protected virtual Action<IServiceCollection>? ConfigureGlobalServices => null;

    // ---- lifecycle ----------------------------------------------------------------------------

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Create schema before anything else (factory seeders also run against same DB).
        await using var db = CreateTypedDbContext();
        await db.Database.EnsureCreatedAsync();

        await OnSchemaCreatedAsync(db);
        await SeedFixtureAsync(db);

        AdminAndUserFactory = NewFactory(TestRoles.AdminAndUser);
        AdminFactory = NewFactory(TestRoles.Admin);
        UserFactory = NewFactory(TestRoles.User);
        RootFactory = NewFactory(TestRoles.Root);
        UnauthenticatedFactory = NewFactory(null);

        // Force the primary host to start (which runs UseFastEndpoints and pins the global command-bus
        // resolver to it) and remember that resolver. It outlives every test, so transient hosts created by
        // CreateFactory can safely restore it after they're disposed.
        _ = AdminAndUserFactory.Services;
        _ = AdminFactory.Services;
        _ = UserFactory.Services;
        _ = RootFactory.Services;
        _ = UnauthenticatedFactory.Services;
        _primaryResolver = FastEndpointsCommandBus.CaptureResolver();
    }

    public ITestClientFactory CreateFactory(string[]? roles, long? userId = null,
            Action<IServiceCollection>? configureServices = null,
            IEnumerable<KeyValuePair<string, string?>>? configOverrides = null,
            bool preserveExecutionContext = false) // Wrap the transient host so that, once the caller disposes it, the FastEndpoints command-bus
        // resolver is re-pinned to the still-alive primary host instead of being left dangling at the
        // disposed transient provider (which would 500 every later command).
    {
        return new ResolverRestoringFactory(
            NewFactory(roles, userId, configureServices, configOverrides, preserveExecutionContext),
            () => FastEndpointsCommandBus.PinResolver(_primaryResolver));
    }

    private TestWebApplicationFactory<TProgram> NewFactory(string[]? roles, long? userId = null, Action<IServiceCollection>? configureServices = null,
        IEnumerable<KeyValuePair<string, string?>>? configOverrides = null, bool preserveExecutionContext = false) =>
        new(ConnectionString, roles, userId, ComposeConfigureServices(configureServices), configOverrides, preserveExecutionContext);

    private Action<IServiceCollection>? ComposeConfigureServices(Action<IServiceCollection>? perCall)
    {
        var global = ConfigureGlobalServices;
        if (global is null)
            return perCall;
        if (perCall is null)
            return global;
        return services =>
        {
            global(services);
            perCall(services);
        };
    }

    public async ValueTask DisposeAsync()
    {
        await UnauthenticatedFactory.DisposeAsync();
        await RootFactory.DisposeAsync();
        await UserFactory.DisposeAsync();
        await AdminFactory.DisposeAsync();
        await AdminAndUserFactory.DisposeAsync();
        await _container.DisposeAsync();
        if (_dataSource != null)
            await _dataSource.DisposeAsync();
    }

    private Respawner? _respawner;

    public async Task ResetAsync()
    {
        _respawner ??= await CreateRespawnerAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);

        await using var db = CreateTypedDbContext();
        await SeedFixtureAsync(db);
        await AfterResetAsync(db);
    }

    private async Task<Respawner> CreateRespawnerAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await Respawner.CreateAsync(conn, new RespawnerOptions { DbAdapter = DbAdapter.Postgres });
    }

    DbContext IPostgresFixture.CreateDbContext() => CreateTypedDbContext();

    /// <summary>A fresh, strongly-typed portal DbContext.</summary>
    public TDbContext CreateDbContext() => CreateTypedDbContext();

    private TDbContext CreateTypedDbContext()
    {
        _dataSource ??= new NpgsqlDataSourceBuilder(ConnectionString).EnableDynamicJson().Build();
        var options = new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(_dataSource)
            .UseSnakeCaseNamingConvention()
            .UseLoggerFactory(NullLoggerFactory.Instance)
            .Options;

        return NewDbContext(options, new FakeLoggedUserService());
    }

    /// <summary>
    /// Wraps a transient per-test factory so that disposing it re-pins the FastEndpoints command-bus
    /// resolver back to the long-lived primary host. Without this, the global resolver keeps pointing at the
    /// disposed transient host and every subsequent command throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    private sealed class ResolverRestoringFactory(ITestClientFactory inner, Action restoreResolver) : ITestClientFactory
    {
        public HttpClient CreateClient() => inner.CreateClient();

        public HttpClient CreateClient(WebApplicationFactoryClientOptions options) => inner.CreateClient(options);

        public IServiceProvider Services => inner.Services;

        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            restoreResolver();
        }
    }
}