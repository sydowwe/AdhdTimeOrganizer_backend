using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.command;

/// <summary>
/// The one endpoint on an <c>IActivityIdRequest</c> that needs no
/// <c>EnsureOwnsActivityAsync</c> guard in <c>BeforeMapping</c>: <see cref="CreateMemoryAnchorValidator"/>
/// already loads the activity as <c>a.Id == req.ActivityId &amp;&amp; a.UserId == userId</c> on its way
/// to the backlog/bucket-list rule, and answers "Activity not found." for a foreign id. Adding the
/// guard here would be a second round trip for a fact the request has already established. The
/// <b>update</b> endpoint does need it — its validator has no such rule.
/// </summary>
public class CreateMemoryAnchorEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateMemoryAnchorValidator>();
    }
}