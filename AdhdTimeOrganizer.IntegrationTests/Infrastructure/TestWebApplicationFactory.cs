using System.Security.Cryptography;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("JWT_ISSUER", "test-issuer");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "test-audience");
        Environment.SetEnvironmentVariable("PAGE_URL", "https://localhost");
        Environment.SetEnvironmentVariable("DB_HOST", "localhost");
        Environment.SetEnvironmentVariable("DB_PORT", "5432");
        Environment.SetEnvironmentVariable("DB_USER", "test");
        Environment.SetEnvironmentVariable("DB_PASSWORD", "test");
        Environment.SetEnvironmentVariable("DB_NAME", "test");
        Environment.SetEnvironmentVariable("LOG_DB_USER", "test");
        Environment.SetEnvironmentVariable("LOG_DB_PASSWORD", "test");

        Directory.CreateDirectory("secrets");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText("secrets/ec_private.pem", ecdsa.ExportECPrivateKeyPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString(),
                        b => b.MigrationsAssembly(typeof(Program).Assembly.FullName))
                    .UseSnakeCaseNamingConvention()
                    .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>());

            var recaptchaDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IGoogleRecaptchaService));
            if (recaptchaDescriptor != null) services.Remove(recaptchaDescriptor);

            var recaptchaMock = new Mock<IGoogleRecaptchaService>();
            recaptchaMock
                .Setup(s => s.VerifyRecaptchaAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Successful());
            services.AddSingleton(recaptchaMock.Object);

            services.Configure<CookiePolicyOptions>(o =>
            {
                o.Secure = CookieSecurePolicy.SameAsRequest;
                o.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = "test-issuer";
                options.TokenValidationParameters.ValidAudience = "test-audience";
                var pem = File.ReadAllText("secrets/ec_private.pem");
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                options.TokenValidationParameters.IssuerSigningKey = new ECDsaSecurityKey(ecdsa);
                options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256];
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
