using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

/// <summary>
/// No <c>BeforeMapping</c> ownership guard — <see cref="CreateActivityProjectProfileValidator"/> already
/// establishes it. See <c>CreateActivityBacklogProfileEndpoint</c>.
/// </summary>
public class CreateActivityProjectProfileEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<ActivityProjectProfile, ActivityProjectProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateActivityProjectProfileValidator>();
    }
}
