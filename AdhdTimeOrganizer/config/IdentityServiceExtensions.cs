using System.Security.Claims;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.extService.user.auth;
using AdhdTimeOrganizer.infrastructure.security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.security;

namespace AdhdTimeOrganizer.config;

public static class IdentityServiceExtensions
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IEcdsaKeyProvider>((options, keyProvider) =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Helper.GetEnvVar("JWT_ISSUER"),
                    ValidAudience = Helper.GetEnvVar("JWT_AUDIENCE"),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyProvider.GetSigningKey(),
                    ValidAlgorithms = [keyProvider.SecurityAlgorithm]
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Priority 1: Authorization Bearer header (for extension)
                        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                        {
                            context.Token = authHeader.Substring("Bearer ".Length).Trim();
                            return Task.CompletedTask;
                        }

                        // Priority 2: Cookie (for web)
                        if (context.Request.Cookies.ContainsKey("auth-token"))
                            context.Token = context.Request.Cookies["auth-token"];

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        // Set custom header for expired tokens
                        if (context.Exception is SecurityTokenExpiredException)
                            context.Response.Headers.Append("X-Token-Expired", "true");
                        return Task.CompletedTask;
                    }
                };
            });

        // Register authorization handler
        services.AddSingleton<IAuthorizationHandler, ExtensionClientAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Attached by the endpoint configurator in Program.cs to every endpoint WITHOUT
            // [AllowExtensionClients], which is what actually makes extension access deny-by-default.
            // No RequireAuthenticatedUser: it lands on anonymous endpoints too, and an anonymous
            // caller carries no client_type claim, so the handler lets them through.
            options.AddPolicy(ExtensionClientPolicies.DenyExtensionClients, policy =>
                policy.AddRequirements(new ExtensionClientRequirement(false)));

            // Default policy: deny extension clients (for web-only endpoints)
            options.AddPolicy(ExtensionClientPolicies.WebOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ExtensionClientRequirement(false));
            });

            // Policy for extension clients only
            options.AddPolicy(ExtensionClientPolicies.ExtensionOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaims.ClientType, AuthClaims.ExtensionClientType);
            });

            // Policy for activity tracking endpoints (allows extension clients)
            options.AddPolicy(PortalAuthorizationPolicies.ActivityTracking, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(ExtensionRoleClaimsProvider.ExtensionRole);
                policy.AddRequirements(new ExtensionClientRequirement(true));
            });

            // Set fallback policy: all authenticated endpoints deny extension clients by default
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ExtensionClientRequirement(false))
                .Build();
        });

        services.AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;

                // options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.";
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                options.ClaimsIdentity.UserNameClaimType = ClaimTypes.NameIdentifier;
                options.ClaimsIdentity.EmailClaimType = ClaimTypes.Email;
            }).AddRoles<UserRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager<SignInManager<User>>()
            .AddRoleManager<RoleManager<UserRole>>()
            .AddUserManager<UserManager<User>>();

        services.Configure<IdentityOptions>(options => { options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider; });
        return services;
    }
}