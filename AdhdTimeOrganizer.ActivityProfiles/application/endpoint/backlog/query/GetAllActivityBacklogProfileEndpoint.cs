using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GetAllActivityBacklogProfileEndpoint(DbContext dbContext) : BaseGetAllEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse>(dbContext)
{

}