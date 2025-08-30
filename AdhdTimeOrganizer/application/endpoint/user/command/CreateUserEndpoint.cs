using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.command;

public class CreateUserEndpoint(UserManager<User> userManager, RoleManager<UserRole> roleManager, UserMapper mapper)
    : Endpoint<CreateUserRequest, UserResponse>
{
    public override void Configure()
    {
        Post("user");
        Roles(EndpointHelper.GetAdminOrHigherRoles());
        Summary(s => { s.Summary = "Create a new user (Admin only)"; });
    }

    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
    {
        // Check if user with email already exists
        var existingUser = await userManager.FindByEmailAsync(req.Email);
        if (existingUser is not null)
        {
            AddError(r => r.Email, "A user with this email already exists.");
            await SendErrorsAsync(400, ct);
            return;
        }

        // Create the user entity
        var user = mapper.ToEntity(req);
        user.EmailConfirmed = true; // Admin-created users are automatically confirmed
        user.UserName = req.Email.Split('@')[0];

        // Create user with password
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }

            await SendErrorsAsync(400, ct);
            return;
        }

        var role = await roleManager.FindByNameAsync("User");
        if (role is null)
        {
            await SendErrorsAsync(404, ct);
            return;
        }

        // Assign default role if specified
        if (!string.IsNullOrEmpty(role.Name))
        {
            await userManager.AddToRoleAsync(user, role.Name);
        }

        var response = mapper.ToResponse(user);
        await SendAsync(response, 201, ct);
    }
}