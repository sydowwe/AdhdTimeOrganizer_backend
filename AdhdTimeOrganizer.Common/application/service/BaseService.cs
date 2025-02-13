using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Common.application.service;

public class BaseService<TEntity>(
    IMapper autoMapper
) : IBaseService<TEntity>
    where TEntity : IEntity
{
    protected readonly IMapper mapper = autoMapper;
    //TODO odstranit automapper a potom aj EF dependency
    protected async Task<List<TR>> ProjectFromQueryToListAsync<TR>(IQueryable<BaseEntity> query)
        where TR : class
    {
        return await query.ProjectTo<TR>(mapper.ConfigurationProvider).ToListAsync();
    }


    protected ServiceResult ProcessRepositoryResult(RepositoryResult result)
    {
        return result.Failed
            ? HandleFailedRepositoryResult(result)
            : ServiceResult.Successful();
    }
    protected ServiceResult<TE> ProcessRepositoryResultWithEntity<TE>(IRepositoryResult<TE> result)
    where TE : IEntity
    {
        return result.Failed
            ? ServiceResult<TE>.Error(MapErrorType(result.ErrorType), result.ErrorMessage)
            : ServiceResult<TE>.Successful(result.Data);
    }

    protected ServiceResult<TR> ProcessRepositoryResult<TR>(IRepositoryResult<IEntity> result)
        where TR : IMyResponse
    {
        return result.Failed
            ? ServiceResult<TR>.Error(MapErrorType(result.ErrorType), result.ErrorMessage)
            : ServiceResult<TR>.Successful(mapper.Map<TR>(result.Data));
    }

    protected ServiceResult<TR> ProcessRepositoryResult<TR>(RepositoryResult result, IEntity responseEntity)
        where TR : IMyResponse
    {
        return result.Failed
            ? ServiceResult<TR>.Error(MapErrorType(result.ErrorType), result.ErrorMessage)
            : ServiceResult<TR>.Successful(mapper.Map<TR>(responseEntity));
    }

    protected ServiceResult HandleFailedRepositoryResult(RepositoryResult result)
    {
        return ServiceResult.Error(MapErrorType(result.ErrorType), result.ErrorMessage);
    }

    protected ServiceResult<TR> HandleFailedRepositoryResult<TR>(RepositoryResult result)
        where TR : notnull
    {
        return ServiceResult<TR>.Error(MapErrorType(result.ErrorType), result.ErrorMessage);
    }

    private static ServiceErrorType MapErrorType(RepositoryErrorType? repositoryErrorType)
    {
        return repositoryErrorType switch
        {
            RepositoryErrorType.NotFound => ServiceErrorType.NotFound,
            RepositoryErrorType.UniqueViolationError => ServiceErrorType.UniqueViolationError,
            RepositoryErrorType.DatabaseError => ServiceErrorType.DatabaseError,
            RepositoryErrorType.ForeignKeyError => ServiceErrorType.ForeignKeyError,
            RepositoryErrorType.ValidationError => ServiceErrorType.ValidationError,
            RepositoryErrorType.DbConcurrencyError => ServiceErrorType.DbConcurrencyError,
            _ => ServiceErrorType.InternalServerError
        };
    }
}