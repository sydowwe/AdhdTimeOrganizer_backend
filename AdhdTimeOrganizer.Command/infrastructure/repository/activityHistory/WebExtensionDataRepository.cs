using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activityHistory;

public class WebExtensionDataRepository(AppCommandDbContext context) : BaseEntityWithActivityRepository<WebExtensionData>(context), IWebExtensionDataRepository;