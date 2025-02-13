using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activity;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Command.infrastructure.repository.@base;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.activity;

public class CategoryRepository(AppCommandDbContext context) : BaseEntityWithUserRepository<Category>(context), ICategoryRepository;