namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The target group a pattern-mapping write carries — what the rule resolves a matched entry to.
/// Implemented by all four mapping requests (desktop / android × create / update).
/// </summary>
/// <remarks>
/// <para>
/// It exists so the two rules that decide whether the group is <em>well-formed</em>
/// (<c>MappingTargetRules.AddMappingTargetRules</c>) and the guard that decides whether its ids are the
/// <em>caller's own</em> (<c>MappingTargetOwnershipExtensions.EnsureOwnsMappingTargetsAsync</c>) are
/// written once and applied to every request that can write these four members. Until CQ-10 the update
/// DTOs carried none of them, so both lived on the create side only — and the moment the members were
/// added to update without them, SEC-2 would have reopened on a surface that did not exist when it was
/// closed.
/// </para>
/// <para>
/// Read-only accessors on purpose: the requests are records with <c>init</c> setters, and nothing should
/// be able to rewrite a target through this interface between validation and the ownership check.
/// </para>
/// </remarks>
public interface IMappingTargetRequest
{
    bool? IsIgnored { get; }
    long? ActivityId { get; }
    long? RoleId { get; }
    long? CategoryId { get; }
}
