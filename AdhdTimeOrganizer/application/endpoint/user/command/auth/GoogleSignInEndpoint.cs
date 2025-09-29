using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.extService.user.auth;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class GoogleSignInEndpoint(
    UserManager<User> userManager,
    AppCommandDbContext dbContext,
    UserMapper mapper,
    IJwtService jwtService,
    IUserDefaultsService userDefaultsService)
    : Endpoint<GoogleSignInRequest, GoogleSignInResponse>
{
    public override void Configure()
    {
        Post("user/google/sign-in");
        AllowAnonymous();
        Throttle(hitLimit: 10, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Sign in with Google OAuth"; });
    }

    public override async Task HandleAsync(GoogleSignInRequest req, CancellationToken ct)
    {
        var googleSignInResult = await GoogleSignInService.GetUserInfoFromGoogleSignInCode(req.Code);
        if (googleSignInResult.Failed)
        {
            AddError("Failed to verify Google sign-in code.");
            await SendErrorsAsync(400, ct);
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
                TwoFactorEnabled = true,
                CurrentLocale = AvailableLocales.Sk,
                GoogleOAuthUserId = googleUserId
            };


            user = await Register(registrationRequest, ct);
            if (user is null)
                return;
        }
        else if (!user.HasGoogleOAuth)
        {
            AddError("A password is already associated with this email. Log in and link Google in account settings.");
            await SendErrorsAsync(409, ct);
            return;
        }
        else if (user.GoogleOAuthUserId != googleUserId)
        {
            AddError("User already exists with a different Google account.");
            await SendErrorsAsync(409, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(true, AuthMethodEnum.Google, user, userManager, HttpContext);

        var response = new GoogleSignInResponse
        {
            Email = googleInfo.Email,
            // CurrentLocale = user.CurrentLocale
        };

        await SendOkAsync(response, ct);
    }

    private async Task<User?> Register(GoogleAuthRegistrationRequest req, CancellationToken ct)
    {
        var newUser = mapper.ToEntityWithGoogleId(req);

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        var identityResult = await userManager.CreateAsync(newUser);
        if (!identityResult.Succeeded)
        {
            var duplicate = identityResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            var msg = duplicate
                ? $"User already exists with EMAIL: {newUser.Email}"
                : "Failed to register user: " + string.Join(", ", identityResult.Errors.Select(e => e.Description));
            AddError(msg);
            await SendErrorsAsync(duplicate ? 409 : 400, ct);
            return null;
        }

        try
        {
            var setDefaultsResult = await userDefaultsService.CreateDefaultsAsync(newUser.Id, ct);
            if (setDefaultsResult.Failed)
            {
                AddError(setDefaultsResult.ErrorMessage!);
                await SendErrorsAsync(500, ct);
                return null;
            }

            await tx.CommitAsync(ct);
            return newUser;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            AddError(ex.Message);
            await SendErrorsAsync(500, ct);
            return null;
        }
    }
}