namespace AdhdTimeOrganizer.application.dto.response.user;

/// <summary><c>DELETE user/account</c> (P3-07): when the account will be deleted, unless it is kept before then.</summary>
public record AccountDeletionScheduledResponse(DateTime DeletionScheduledFor);
