using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sydowwe.Framework.Testing;

public class RoleTestAuthHandlerOptions : AuthenticationSchemeOptions
{
    public string[] Roles { get; set; } = [];
    public long UserId { get; set; } = FakeLoggedUserService.TestUserId;
    public string Email { get; set; } = FakeLoggedUserService.TestUserEmail;
}

public class RoleTestAuthHandler(
    IOptionsMonitor<RoleTestAuthHandlerOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<RoleTestAuthHandlerOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var opts = Options;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, opts.UserId.ToString()),
            new(ClaimTypes.Email, opts.Email)
        };
        foreach (var role in opts.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}