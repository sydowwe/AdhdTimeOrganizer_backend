using Microsoft.AspNetCore.Authorization;

namespace AdhdTimeOrganizer.infrastructure.security;

/// <summary>
/// Authorization requirement that checks if extension clients are allowed.
/// By default, extension clients are denied unless the endpoint explicitly allows them.
/// </summary>
public class ExtensionClientRequirement(bool allowExtensionClients = false) : IAuthorizationRequirement
{
    public bool AllowExtensionClients { get; } = allowExtensionClients;
}

/// <summary>
/// Authorization handler that enforces extension client restrictions.
/// </summary>
public class ExtensionClientAuthorizationHandler : AuthorizationHandler<ExtensionClientRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ExtensionClientRequirement requirement)
    {
        // Check if user is an extension client
        var clientTypeClaim = context.User.FindFirst("client_type");
        var isExtensionClient = clientTypeClaim?.Value == "Extension";

        if (isExtensionClient)
        {
            // Extension client detected
            if (requirement.AllowExtensionClients)
            {
                // This endpoint allows extension clients
                context.Succeed(requirement);
            }
            else
            {
                // This endpoint does NOT allow extension clients - deny
                context.Fail();
            }
        }
        else
        {
            // Not an extension client (web client) - always allow
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marker attribute indicating this endpoint is intended to be accessible by extension clients.
/// This attribute has no enforcement effect on its own — authorization is controlled by
/// the policy applied in the endpoint's <c>Configure()</c> method (e.g. "ActivityTracking").
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class AllowExtensionClientsAttribute : Attribute
{
}

