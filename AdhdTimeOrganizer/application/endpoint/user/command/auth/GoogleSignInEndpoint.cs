using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class GoogleSignInEndpoint(
    UserManager<User> userManager,
    AppDbContext dbContext,
    IJwtService jwtService,
    IUserDefaultsService userDefaultsService,
    IGoogleSignInService googleSignInService)
    : Endpoint<GoogleSignInRequest, GoogleSignInResponse>
{
    public override void Configure()
    {
        Post("/auth/login/google");
        AllowAnonymous();
        Throttle(hitLimit: 10, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Sign in with Google OAuth"; });
    }

    public override async Task HandleAsync(GoogleSignInRequest req, CancellationToken ct)
    {
        var googleSignInResult = await googleSignInService.GetUserInfoFromGoogleSignInCode(req.Code);
        if (googleSignInResult.Failed)
        {
            AddError("Failed to verify Google sign-in code.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var googleInfo = googleSignInResult.Data;
        var googleUserId = googleInfo.UserId;

        var user = await userManager.FindByEmailAsync(googleInfo.Email);
        if (user is null)
        {
            var registrationRequest = new GoogleAuthRegistrationRequest
            {
                Email = googleInfo.Email,
                Timezone = req.Timezone,
                TwoFactorEnabled = false,
                CurrentLocale = AvailableLocales.En,
                GoogleOAuthUserId = googleUserId
            };

            user = await Register(registrationRequest, ct);
            if (user is null)
                return;
        }
        else if (!user.HasGoogleOAuth || user.GoogleOAuthUserId != googleUserId)
        {
            AddError("Could not sign in with Google.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(true, AuthMethodEnum.Google, user, HttpContext);

        var response = new GoogleSignInResponse
        {
            Email = googleInfo.Email,
            CurrentLocale = user.CurrentLocale
        };

        await Send.OkAsync(response, ct);
    }

    private async Task<User?> Register(GoogleAuthRegistrationRequest req, CancellationToken ct)
    {
        var newUser = req.ToEntity;

        newUser.GoogleOAuthUserId = req.GoogleOAuthUserId;
        newUser.EmailConfirmed = true;

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var identityResult = await userManager.CreateAsync(newUser);
            if (!identityResult.Succeeded)
            {
                var duplicate = identityResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
                AddError(duplicate
                    ? "Could not sign in with Google."
                    : "Failed to register user: " + string.Join(", ", identityResult.Errors.Select(e => e.Description)));
                await Send.ErrorsAsync(duplicate ? 409 : 400, ct);
                return null;
            }

            identityResult = await userManager.AddToRoleAsync(newUser, "User");
            if (!identityResult.Succeeded)
            {
                AddError("Failed to add user to role: " + string.Join(", ", identityResult.Errors.Select(e => e.Description)));
                await Send.ErrorsAsync(500, ct);
                return null;
            }

            var setDefaultsResult = await userDefaultsService.CreateDefaultsAsync(newUser.Id, ct);
            if (setDefaultsResult.Failed)
            {
                AddError(setDefaultsResult.ErrorMessage!);
                await Send.ErrorsAsync(500, ct);
                return null;
            }

            await tx.CommitAsync(ct);
            return newUser;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            AddError(ex.Message);
            await Send.ErrorsAsync(500, ct);
            return null;
        }
    }
}