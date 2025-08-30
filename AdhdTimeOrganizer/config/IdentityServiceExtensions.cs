using System.Security.Claims;
using System.Security.Cryptography;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.config;

public static class IdentityServiceExtensions
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        var ecdsaPublicPem = File.ReadAllText("secrets/ec_public.pem");

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(ecdsaPublicPem);

        var securityKey = new ECDsaSecurityKey(ecdsa)
        {
            KeyId = "your-key-id"
        };

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Helper.GetEnvVar("JWT_ISSUER"),
                    ValidAudience = Helper.GetEnvVar("JWT_AUDIENCE"),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = securityKey,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Try to get token from cookie first, then from Authorization header
                        if (context.Request.Cookies.ContainsKey("auth-token"))
                        {
                            context.Token = context.Request.Cookies["auth-token"];
                        }

                        return Task.CompletedTask;
                    }
                };
            });


        services.AddAuthorization();

        services.AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 4;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.";
                options.User.RequireUniqueEmail = true;
                options.ClaimsIdentity.UserNameClaimType = ClaimTypes.NameIdentifier;
                options.ClaimsIdentity.EmailClaimType = ClaimTypes.Email;
            }).AddRoles<UserRole>()
            .AddEntityFrameworkStores<AppCommandDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager<SignInManager<User>>()
            .AddRoleManager<RoleManager<UserRole>>()
            .AddUserManager<UserManager<User>>();

        services.ConfigureApplicationCookie(options =>
        {
            // options.Cookie.SameSite = SameSiteMode.None;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60); // Cookie expiration time
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        return services;
    }
}