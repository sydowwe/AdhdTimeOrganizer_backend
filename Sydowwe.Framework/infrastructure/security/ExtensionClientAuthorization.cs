using Microsoft.AspNetCore.Authorization;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.security;

/// <summary>
/// Authorization requirement that checks whether extension clients are allowed.
/// By default, extension clients are denied unless the endpoint explicitly allows them.
/// </summary>
public class ExtensionClientRequirement(bool allowExtensionClients = false) : IAuthorizationRequirement
{
    public bool AllowExtensionClients { get; } = allowExtensionClients;
}

/// <summary>
/// Enforces the extension-client restriction. The counterpart to <c>JwtService</c>, which mints the
/// <see cref="AuthClaims.ClientType"/> claim this reads — writer and reader live in the same assembly
/// deliberately, so the claim name cannot drift between them.
/// </summary>
public class ExtensionClientAuthorizationHandler : AuthorizationHandler<ExtensionClientRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ExtensionClientRequirement requirement)
    {
        var clientTypeClaim = context.User.FindFirst(AuthClaims.ClientType);
        var isExtensionClient = clientTypeClaim?.Value == AuthClaims.ExtensionClientType;

        if (isExtensionClient)
        {
            if (requirement.AllowExtensionClients)
                context.Succeed(requirement);
            else
                context.Fail();
        }
        else
        {
            // Not an extension client (web client) - always allow.
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Opts an endpoint in to browser-extension / desktop clients. <b>Deny-by-default:</b> the host's
/// endpoint configurator attaches <see cref="ExtensionClientPolicies.DenyExtensionClients"/> to every
/// endpoint that does *not* carry this attribute, so a token minted with
/// <c>client_type=Extension</c> is refused there.
///
/// <para>Putting the attribute on an endpoint only lifts that blanket refusal — it grants nothing.
/// Roles and any policy the endpoint sets itself still apply.</para>
///
/// <para>Do not rely on <c>AuthorizationOptions.FallbackPolicy</c> for this: the configurator gives
/// every endpoint role metadata, and an endpoint that carries any authorization metadata never falls
/// back. That is precisely why the deny has to be attached per endpoint.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class AllowExtensionClientsAttribute : Attribute
{
}

/// <summary>
/// Policy names for the client-type gate. These three are framework-level because they are about the
/// *kind of client*, which this framework models end to end. Policy names that encode a deployment's
/// own roles or features are not framework concerns and stay in the host.
/// </summary>
public static class ExtensionClientPolicies
{
    /// <summary>
    /// Refuses extension clients. Deliberately carries no <c>RequireAuthenticatedUser</c>: it is
    /// attached to nearly every endpoint including anonymous ones, and an anonymous caller has no
    /// <see cref="AuthClaims.ClientType"/> claim, so the handler passes them through.
    /// </summary>
    public const string DenyExtensionClients = "DenyExtensionClients";

    /// <summary>Web-only; kept for endpoints that want to state the requirement explicitly.</summary>
    public const string WebOnly = "WebOnly";

    public const string ExtensionOnly = "ExtensionOnly";
}