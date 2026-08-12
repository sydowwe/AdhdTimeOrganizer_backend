using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class ActivityLocationTypeConfiguration : BaseLookupWithUserConfiguration<User, ActivityLocationType>;