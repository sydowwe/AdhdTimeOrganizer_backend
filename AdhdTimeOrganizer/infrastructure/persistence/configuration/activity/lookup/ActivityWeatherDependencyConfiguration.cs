using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity.lookup;

public class ActivityWeatherDependencyConfiguration : BaseLookupWithUserConfiguration<User, ActivityWeatherDependency>;