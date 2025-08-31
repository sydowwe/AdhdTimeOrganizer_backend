using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AdhdTimeOrganizer.application.extensions;

public static class ClaimsPrincipalExtensions
{
    public static long GetId(this ClaimsPrincipal principal)
    {
        var userIdString = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userIdString))
        {
            throw new UnauthorizedAccessException("User ID not found in token claims");
        }

        if (!long.TryParse(userIdString, out var userId))
        {
            throw new InvalidOperationException($"Unable to parse user ID '{userIdString}' as long");
        }

        return userId;
    }

    public static long? GetIdOrNull(this ClaimsPrincipal principal)
    {
        var userIdString = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                          ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(userIdString, out var userId) ? userId : null;
    }
}