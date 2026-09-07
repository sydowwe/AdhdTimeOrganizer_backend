using System.Linq.Expressions;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerSettings;

/// <summary>
/// Upserts the caller's planner settings. The row is created on first write exactly as
/// <c>GetPlannerSettingsEndpoint</c> creates it on first read, so both halves of the pair race against the
/// unique <c>user_id</c> index.
/// <para>
/// The upsert shape is <see cref="BaseUserSettingsUpsertEndpoint{TEntity,TRequest}"/>'s, shared with the
/// Routines and module settings endpoints that had this same hand-written body. Both write races are handled
/// there: a lost first-write insert recovers onto the winner's row, and a lost <c>row_version</c> race against
/// a row that already exists is a 409 rather than the 500 the hand-written copy answered.
/// </para>
/// </summary>
public class UpdatePlannerSettingsEndpoint(DbContext dbContext)
    : BaseUserSettingsUpsertEndpoint<UserPlannerSettings, UserPlannerSettingsRequest>(dbContext)
{
    protected override string Route => "/planner/settings";

    public override void Configure()
    {
        base.Configure();
        Validator<UserPlannerSettingsValidator>();
        Summary(s =>
        {
            s.Summary = "Update planner settings for the current user";
            s.Response(204, "Success");
            s.Response(400, "Bad request");
            s.Response(404, "DefaultApplyTemplateId names no template of this user's");
            s.Response(409, "Lost a concurrent write — re-read and retry");
        });
    }

    // DefaultApplyTemplateId is a client-supplied id and the FK proves only that the row exists. The response
    // returns it bare, so a stranger's id leaks nothing — it just stores a pointer at a row this user can never
    // resolve, and the day-planner's default-apply then silently does nothing.
    protected override async Task<bool> BeforeUpsertAsync(UserPlannerSettingsRequest req, CancellationToken ct) =>
        req.DefaultApplyTemplateId is not { } templateId
        || await this.EnsureOwnsDayTemplateAsync(DbContext, templateId,
            nameof(UserPlannerSettingsRequest.DefaultApplyTemplateId), ct);

    protected override Expression<Func<UserPlannerSettings, bool>> MatchRow(long userId, UserPlannerSettingsRequest req) =>
        s => s.UserId == userId;

    // UserId explicitly: this is one of the two insert paths, and it must not depend on the ambient user.
    protected override UserPlannerSettings NewRow(long userId, UserPlannerSettingsRequest req)
    {
        var row = new UserPlannerSettings { UserId = userId };
        req.UpdateEntity(row);
        return row;
    }

    protected override void Apply(UserPlannerSettings row, UserPlannerSettingsRequest req) => req.UpdateEntity(row);
}
