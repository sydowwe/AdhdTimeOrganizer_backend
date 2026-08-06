using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.request.@interface;

public interface ICreateRequest
{
}

// NOTE: a [JsonIgnore] here would NOT keep ToEntity out of the generated Swagger schema — STJ/NJsonSchema read
// JsonIgnore from the concrete implementing property, not the interface. ToEntity is excluded globally instead by
// RemoveToEntitySchemaProcessor (MojaDigitalnaFirma.AdminPortal/config/swagger/SwaggerConfig.cs), which strips it
// before FastEndpoints' ValidationSchemaProcessor walks the schema (the entity nav graph it pulls in can have a
// one-to-one cycle that overflows the stack).
public interface ICreateRequest<out TEntity> : ICreateRequest where TEntity : class, IEntityWithId
{
    public TEntity ToEntity { get; }
}