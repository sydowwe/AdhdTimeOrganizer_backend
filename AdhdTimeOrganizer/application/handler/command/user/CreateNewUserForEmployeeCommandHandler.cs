using AdhdTimeOrganizer.application.commands;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.handler.command.user;

public class CreateNewUserForUserCommandHandler(AppCommandDbContext dbContext, RoleManager<UserRole> roleManager, UserManager<User> userManager)
    : ICommandHandler<CreateUserForNewUserCommand, Result<User>>
{
    public async Task<Result<User>> ExecuteAsync(CreateUserForNewUserCommand command, CancellationToken ct)
    {
        var email = command.Username;

        const string roleName = "User";

        var newUser = new User
        {
            UserName = command.Username,
            Email = email,
            EmailConfirmed = true,
            CurrentLocale = AvailableLocales.Sk,
            Timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava"),
        };

        var tempPassword = GenerateTemporaryPassword();
        var result = await userManager.CreateAsync(newUser, tempPassword);
        if (!result.Succeeded)
        {
            return Result<User>.Error(ResultErrorType.IdentityError, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        result = await userManager.AddToRoleAsync(newUser, roleName);
        if (!result.Succeeded)
        {
            return Result<User>.Error(ResultErrorType.IdentityError, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // await userManager.SetAuthenticationTokenAsync(newUser, "EntraId", "access_token", newAccessToken.AccessToken);
        // await userManager.SetAuthenticationTokenAsync(newUser, "EntraId", "refresh_token", newAccessToken.RefreshToken);
        return Result<User>.Successful(newUser);
    }

    private static string GenerateTemporaryPassword()
    {
        const string upperCase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowerCase = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specialChars = "!@#$%^&*";

        const string allChars = upperCase + lowerCase + digits + specialChars;
        var random = new Random();

        var password = new char[8];
        password[0] = upperCase[random.Next(upperCase.Length)];
        password[1] = lowerCase[random.Next(lowerCase.Length)];
        password[2] = digits[random.Next(digits.Length)];
        password[3] = specialChars[random.Next(specialChars.Length)];

        for (var i = 4; i < 8; i++)
        {
            password[i] = allChars[random.Next(allChars.Length)];
        }

        return new string(password.OrderBy(x => random.Next()).ToArray());
    }
}