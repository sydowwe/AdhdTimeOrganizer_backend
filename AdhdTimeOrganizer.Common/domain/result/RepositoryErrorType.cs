namespace AdhdTimeOrganizer.Common.domain.result;

public enum RepositoryErrorType
{
    DbConcurrencyError,
    ValidationError,
    NotFound,
    UniqueViolationError,
    ForeignKeyError,
    NullConstraintError,
    DatabaseError,
    UnknownError,
    DeleteUnsuccessful,
    ExpectedSingleResult
}