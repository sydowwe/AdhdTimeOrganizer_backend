using System.Security.Cryptography;

namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Generates a cryptographically-random 8-character temporary password that satisfies the default
/// Identity complexity rules (upper, lower, digit, special). Shared by new-hire provisioning
/// (<c>CreateNewUserForEmployeeCommandHandler</c>) and login reactivation
/// (<c>UserDeactivationService.ReactivateUserAsync</c>) so the generation logic lives in exactly one place.
/// The password is handed to the admin to give to the employee, who is forced to change it on first login
/// (<c>MustChangePassword</c>).
/// </summary>
public static class TemporaryPasswordGenerator
{
    public static string Generate()
    {
        const string upperCase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowerCase = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specialChars = "!@#$%^&*";
        const string allChars = upperCase + lowerCase + digits + specialChars;

        var password = new char[8];
        password[0] = upperCase[RandomNumberGenerator.GetInt32(upperCase.Length)];
        password[1] = lowerCase[RandomNumberGenerator.GetInt32(lowerCase.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = specialChars[RandomNumberGenerator.GetInt32(specialChars.Length)];

        for (var i = 4; i < 8; i++)
            password[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];

        // Fisher-Yates shuffle using cryptographic randomness
        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}