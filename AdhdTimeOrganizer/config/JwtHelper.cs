using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.config;

public static class JwtHelper
{
    public static string CreateEcdsaJwtToken(List<Claim> claims, DateTime expiry)
    {
        // Read ECDSA private key
        var ecdsaPrivatePem = File.ReadAllText("secrets/ec_private.pem");
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(ecdsaPrivatePem);
        var signingCredentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256);

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

    public static CookieOptions GetJwtCookieOptions(DateTime tokenExpiry)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Only send over HTTPS
            SameSite = SameSiteMode.Strict,
            Expires = tokenExpiry,
            Path = "/",
            IsEssential = true
        };
    }

    public static async Task<List<Claim>> CreateUserClaims(User user, UserManager<User> userManager)
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

    public static void SetAuthCookie(HttpContext httpContext, string token, DateTime expiry)
    {
        httpContext.Response.Cookies.Append("auth-token", token, GetJwtCookieOptions(expiry));
    }
}