using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.command;

/// <summary>
/// No <c>BeforeMapping</c> ownership guard, for the same reason <c>CreateMemoryAnchorEndpoint</c> has
/// none: <see cref="CreateActivityBacklogProfileValidator"/> already roots its round-trip at
/// <c>a.Id == req.ActivityId &amp;&amp; a.UserId == userId</c> and answers "Activity not found." for a
/// foreign id, so a guard here would be a second round trip for a fact the request has established.
/// </summary>
public class CreateActivityBacklogProfileEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<ActivityBacklogProfile, ActivityBacklogProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateActivityBacklogProfileValidator>();
    }
}
