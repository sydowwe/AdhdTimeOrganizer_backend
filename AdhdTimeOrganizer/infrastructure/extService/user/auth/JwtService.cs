using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class JwtService(IEcdsaKeyProvider ecdsaKeyProvider) : IJwtService, IScopedService
{

    public async Task GenerateJwtAndSetAuthCookie(bool stayLoggedIn, AuthMethodEnum authMethod, User user, UserManager<User> userManager, HttpContext httpContext)
    {
        // Create claims using the helper
        var claims = await CreateUserClaims(user, userManager);

        // Add regular login specific claim
        claims.Add(new Claim("auth_method", authMethod.ToString()));

        var tokenExpiry = stayLoggedIn ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1);

        // Create JWT token with ECDSA
        var jwtToken = CreateEcdsaJwtToken(claims, tokenExpiry);

        // Set the cookie using helper
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Only send over HTTPS
            SameSite = SameSiteMode.Strict,
            Expires = tokenExpiry,
            Path = "/",
            IsEssential = true
        };;

        httpContext.Response.Cookies.Append("auth-token", jwtToken, cookieOptions);
    }

    private string CreateEcdsaJwtToken(List<Claim> claims, DateTime expiry)
    {
        var signingCredentials = ecdsaKeyProvider.GetSigningCredentials();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = Helper.GetEnvVar("JWT_ISSUER"),
            Audience = Helper.GetEnvVar("JWT_AUDIENCE"),
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static async Task<List<Claim>> CreateUserClaims(User user, UserManager<User> userManager)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var userRoles = await userManager.GetRolesAsync(user);
        claims.AddRange(userRoles.Select(role => new Claim(ClaimTypes.Role, role)));

        return claims;
    }
}