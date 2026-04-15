# Entity Configuration

When writing EF Core entity configurations, always use the builder extension helpers:

- `AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/EntityBuilderExtensions.cs` — `BaseEntityConfigure()` (sets table name, serial PK, row_version, timestamps), `EnumColumn()`, `FlagsEnumColumn()`
- `AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/EntityWithUserBuilderExtensions.cs` — `IsManyWithOneUser()`, `IsOneWithOneUser()`, and name/text/color/icon base-entity helpers

# FastEndpoints Base Classes

Before writing a custom endpoint, check whether one of the base classes in `AdhdTimeOrganizer/application/endpoint/base/` already covers the pattern. Use them when they fit; write a plain `Endpoint<TReq, TRes>` only when they don't.

**Commands** (`endpoint/base/command/`)
| Class | HTTP | Use when |
|---|---|---|
| `BaseCreateEndpoint<TEntity, TRequest, TMapper>` | POST | Standard create — maps request → entity via `IBaseCreateMapper`, saves, returns new `Id` (201) |
| `BaseUpdateEndpoint<TEntity, TRequest, TMapper>` | PUT `/{id}` | Standard full update via `IBaseUpdateMapper` |
| `BasePatchEndpoint<TEntity, TRequest, TResponse>` | PATCH `/{id}` | Partial update — implement `Mapping(entity, req)` |
| `BaseDeleteEndpoint<TEntity>` | DELETE `/{id}` | Single entity delete by id |
| `BaseBatchDeleteEndpoint<TEntity>` | POST `/batch-delete` | Delete multiple entities by id list |
| `BaseToggleIsHiddenEndpoint<TEntity>` | PATCH `/toggle-is-hidden` | Toggle `IsHidden` on entities implementing `IEntityWithIsHidden` |

**Reads** (`endpoint/base/read/`)
| Class | HTTP | Use when |
|---|---|---|
| `BaseGetAllEndpoint<TEntity, TResponse, TMapper>` | GET | Return all records (optionally filtered by user) |
| `BaseGetByIdEndpoint<TEntity, TResponse, TMapper>` | GET `/{id}` | Return single record by id |
| `BaseGetSelectOptionsEndpoint<TEntity, TMapper>` | GET `/all-options` | Return `id + text` select options |
| `BaseFilterEndpoint<TEntity, TResponse, TFilter, TMapper>` | POST `/filter` | Return list filtered by a custom `IFilterRequest` |
| `BaseSortEndpoint<TEntity, TResponse, TMapper>` | POST `/sort` | Return list with dynamic sort columns |
| `BaseFilterSortEndpoint<TEntity, TResponse, TFilter, TMapper>` | POST `/filter-sort` | Filter + sort without pagination |
| `BaseFetchTableEndpoint<TEntity, TResponse, TFilter, TMapper>` | POST `/filtered-table` | Filter + sort + paginate (full table view) |

All user-scoped base endpoints use `User.GetId()` and filter via `FilteredByUser` (default `true`). Override `AllowedRoles()`, `WithIncludes()`, `Sort()`, or `ApplyCustomFiltering()` as needed.

# DTO Conventions

- **Time values** in requests and responses use `TimeDto` (`AdhdTimeOrganizer/application/dto/dto/TimeDto.cs`) instead of `TimeOnly`. Call `.ToTimeOnly()` when assigning to an entity.
