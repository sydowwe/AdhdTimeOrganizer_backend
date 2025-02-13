using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller.@base;

[ApiController]
[Route("[controller]/[action]")]
[Authorize]
public abstract class BaseController<TEntity, TService>(TService service) : ControllerBase
    where TEntity : BaseEntity
    where TService : IBaseService<TEntity>
{
    private TService _service = service;

    protected static ActionResult HandleFailedServiceResult(ServiceResult serviceResult)
    {
        return serviceResult.ErrorType switch
        {
            ServiceErrorType.NotFound =>
                new NotFoundObjectResult(new { Message = serviceResult.ErrorMessage }),

            ServiceErrorType.UniqueViolationError =>
                new ConflictObjectResult(new { Message = "A conflict occurred: " + serviceResult.ErrorMessage }),

            ServiceErrorType.ForeignKeyError =>
                new BadRequestObjectResult(new { Message = "Invalid reference: " + serviceResult.ErrorMessage }),

            ServiceErrorType.ValidationError =>
                new UnprocessableEntityObjectResult(new { Message = serviceResult.ErrorMessage }),

            ServiceErrorType.DbConcurrencyError =>
                new ConflictObjectResult(new { Message = "Concurrency error: " + serviceResult.ErrorMessage }),

            ServiceErrorType.DatabaseError =>
                new StatusCodeResult(StatusCodes.Status500InternalServerError), // Generic DB error

            _ =>
                new StatusCodeResult(StatusCodes.Status500InternalServerError) // Fallback for unknown errors
        };
    }
}