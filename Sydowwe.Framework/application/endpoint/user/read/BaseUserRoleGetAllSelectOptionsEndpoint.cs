using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.read;

/// <summary>
/// Select options over <see cref="UserRole"/>. Admin surface — role lists are not something a plain
/// user needs.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts that want it derive a thin subclass; this
/// portal currently doesn't expose one.</para>
/// </summary>
public abstract class BaseUserRoleGetAllSelectOptionsEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<UserRole>(dbContext)
{
    protected override string[] AllowedRoles() => this.GetAdminRole();

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<UserRole> query) =>
        query.Select(e => new SelectOptionResponse(e.Id, e.Name!));
}