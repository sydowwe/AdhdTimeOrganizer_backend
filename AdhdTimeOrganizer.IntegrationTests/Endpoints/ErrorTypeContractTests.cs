using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B18 — the <c>errorType</c> field on the error body, and the only thing that proves it is there: an
/// actual response.
/// </summary>
/// <remarks>
/// <para>
/// Four different situations answer 409 (<see cref="ResultErrorType.DbConcurrencyError"/>,
/// <see cref="ResultErrorType.DbUniqueViolationError"/>, <see cref="ResultErrorType.DbForeignKeyError"/>
/// and a business <see cref="ResultErrorType.Conflict"/>) and three answer 403. The status alone cannot
/// tell "refresh and retry" from "pick another name" from "remove the dependents first", so the client
/// used to word all 409s as a concurrency conflict or re-derive the distinction per call site.
/// </para>
/// <para>
/// <b>This is a silent mechanism, which is why these assert on the body.</b> The type travels on
/// <c>HttpContext.Items</c> and is only rendered by the <c>Errors.ResponseBuilder</c> the host installs
/// in <c>Program.cs</c>. Drop that one line and every endpoint still stamps its type, every status stays
/// right, nothing logs and nothing throws — the field just stops appearing. A route- or status-level
/// test would stay green through that.
/// </para>
/// <para>
/// The statuses asserted here are the ones that were already being sent; B18 changed none of them, and
/// <c>RoutineTimePeriodUniqueIndexTests</c> keeps pinning the 409s independently.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ErrorTypeContractTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private static async Task<string?> ReadErrorTypeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("errorType", out var errorType) ? errorType.GetString() : null;
    }

    private static object NewPeriod(string text, int lengthInDays) => new
    {
        Text = text,
        Color = "#abcdef",
        LengthInDays = lengthInDays,
        StreakThreshold = 90,
        StreakGraceDays = 0,
        ResetAnchorDay = 1,
        HistoryDepth = 16
    };

    [Fact(DisplayName = "A unique violation 409 says DbUniqueViolationError")]
    public async Task UniqueViolation_CarriesItsOwnErrorType()
    {
        await CreateClient().PostAsJsonAsync("api/routine-time-period", NewPeriod("Duplicated", 3), JsonOpts, CancellationToken);

        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", NewPeriod("Duplicated", 9), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.DbUniqueViolationError),
            "the client words a name collision as 'pick another value', not as 'someone else changed this row'");
    }

    /// <summary>
    /// The pre-emptive FK refusal in <c>DeleteActivityRoleEndpoint</c>: the role still has activities, and
    /// deleting it would cascade through every activity FK in the solution.
    /// </summary>
    [Fact(DisplayName = "A role that still has activities 409s with DbForeignKeyError")]
    public async Task ForeignKeyRefusal_CarriesItsOwnErrorType()
    {
        long roleId;
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole { UserId = UserId, Name = $"Occupied {Guid.NewGuid():N}", Color = "#112233" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);

            db.Set<Activity>().Add(new Activity { UserId = UserId, Name = "Dependent", RoleId = role.Id });
            await db.SaveChangesAsync(CancellationToken);
            roleId = role.Id;
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.DbForeignKeyError),
            "the user can clear this one by moving the activities — the client says so, which needs the type");
    }

    /// <summary>
    /// Same endpoint, same 409, different situation: a system role can never be deleted, so the client must
    /// not offer "remove the dependents first". This is the pair that makes the field load-bearing.
    /// </summary>
    [Fact(DisplayName = "A system role 409s with Conflict, distinct from the FK refusal on the same endpoint")]
    public async Task BusinessConflict_CarriesItsOwnErrorType()
    {
        long roleId;
        await using (var db = CreateDbContext())
        {
            var existing = await db.Set<ActivityRole>()
                .FirstOrDefaultAsync(r => r.UserId == UserId && r.SystemKey != null, CancellationToken);

            if (existing is null)
            {
                existing = new ActivityRole
                {
                    UserId = UserId,
                    Name = $"System {Guid.NewGuid():N}",
                    Color = "#112233",
                    SystemKey = SystemActivityRole.RoutineTask
                };
                db.Set<ActivityRole>().Add(existing);
                await db.SaveChangesAsync(CancellationToken);
            }

            roleId = existing.Id;
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.Conflict),
            "nothing the user can do clears a system role, so it must not read as the FK refusal above");
    }

    /// <summary>
    /// A 409 from outside the CRUD bases. The tracker-mapping pre-check (<c>MappingPatternUniquenessExtensions</c>)
    /// writes its own response through <c>HttpContext.Response.SendErrorsAsync</c>, not the base's
    /// <c>ErrorStatus</c> path, and it used to pick the status with a bare <c>EndpointHelper.ToStatusCode</c> —
    /// naming the type to choose the 409 and then dropping it on the floor. Pinned here so the hand-rolled
    /// senders stay on the contract, not only the eight framework bases.
    /// </summary>
    [Fact(DisplayName = "A hand-rolled 409 outside the CRUD bases still says DbUniqueViolationError")]
    public async Task HandRolledUniqueViolation_CarriesItsOwnErrorType()
    {
        const string route = "/api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern";
        var client = CreateUserRoleClient();
        var body = new { processName = $"dup-{Guid.NewGuid():N}.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true };

        (await client.PostAsJsonAsync(route, body, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.PostAsJsonAsync(route, body, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorTypeAsync(response)).Should().Be(nameof(ResultErrorType.DbUniqueViolationError),
            "the pre-check knows it is a duplicate; the client must not have to word it as a stale-row conflict");
    }

    /// <summary>
    /// FluentValidation 400s stay exactly as they are: their <c>errors</c> map already names the offending
    /// fields, and the client's per-field rendering must not start branching on a type that isn't there.
    /// </summary>
    [Fact(DisplayName = "A validation 400 carries no errorType at all, only its errors map")]
    public async Task ValidationFailure_OmitsErrorType()
    {
        // StreakThreshold is InclusiveBetween(1, 100) in RoutineTimePeriodValidator, so 0 fails the
        // validator and never reaches the database.
        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "Invalid",
            Color = "#abcdef",
            LengthInDays = 3,
            StreakThreshold = 0,
            StreakGraceDays = 0,
            ResetAnchorDay = 1,
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("errorType", out _).Should().BeFalse(
            "absent, not null — the field is only written when the server actually knows the type");
        json.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty("the 400 body's shape is unchanged by B18");
    }
}

/// <summary>
/// The concurrency arm of the same contract, pinned off the HTTP path because no request DTO implements
/// <c>IUpdateRequestWithRowVersion</c> yet, so a <c>DbUpdateConcurrencyException</c> is currently
/// unreachable through an endpoint. When one does, this should become a request-level test like its
/// siblings above.
/// </summary>
public class ErrorTypeMappingTests
{
    [Fact(DisplayName = "A concurrency conflict is still 409 and names itself DbConcurrencyError")]
    public void ConcurrencyConflict_MapsTo409_AndNamesItself()
    {
        var result = DbUtils.HandleException(new DbUpdateConcurrencyException(), "Update");

        result.ErrorType.Should().Be(ResultErrorType.DbConcurrencyError);
        EndpointHelper.ToStatusCode(result.ErrorType).Should().Be(409, "B18 adds a field, it does not move a status");

        var response = new TypedErrorResponse([], 409, result.ErrorType);

        response.ErrorType.Should().Be(nameof(ResultErrorType.DbConcurrencyError),
            "the wire value is the enum member name — the client branches on it and must never word-match the message");
    }
}

/// <summary>
/// The guard for the bypass that the request-level tests above cannot see: an endpoint that picks its status
/// with <c>EndpointHelper.ToStatusCode(...)</c> directly instead of <c>this.ErrorStatus(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two are identical on the wire except for one thing — <c>ErrorStatus</c> also stamps the type onto
/// <c>HttpContext.Items</c> for the response builder to render. A direct <c>ToStatusCode</c> call therefore
/// sends the right status with no <c>errorType</c>, which every status-asserting test is blind to and which
/// the client cannot distinguish from "the server had no better information". Eleven call sites had drifted
/// that way when B18 landed, two of them naming <see cref="ResultErrorType.DbUniqueViolationError"/> to pick
/// the 409 and then throwing the name away.
/// </para>
/// <para>
/// So this is a source scan, shaped like <c>SliceDependencyTests</c>: the only production files allowed to
/// spell out the call are the mapping itself and the <c>ErrorStatus</c> wrapper that adds the stamp. Tests
/// and the testing packages are excluded because they assert on the mapping legitimately.
/// </para>
/// </remarks>
public class ErrorStatusBypassGuardTests
{
    private const string BypassCall = "EndpointHelper.ToStatusCode(";

    /// <summary>The only production files that may call the mapping directly.</summary>
    private static readonly string[] AllowedFiles =
    [
        Path.Combine("framework", "Sydowwe.Framework", "domain", "helper", "EndpointHelper.cs"),
        Path.Combine("framework", "Sydowwe.Framework", "application", "error", "ResultErrorTypeExtensions.cs")
    ];

    /// <summary>
    /// Build output, the IDE's and the CLI's alike, plus the foreign reference code CLAUDE.md says not to
    /// extend, and every test project — a test asserting <c>ToStatusCode(x).Should().Be(409)</c> is fine.
    /// </summary>
    private static readonly string[] ExcludedSegments =
    [
        "bin", "obj", "artifacts-cli", ".git", "node_modules", "reference",
        "AdhdTimeOrganizer.IntegrationTests", "Sydowwe.Framework.Testing", "Sydowwe.Notifications.Testing"
    ];

    [Fact(DisplayName = "No endpoint picks its status with a bare EndpointHelper.ToStatusCode — it must go through ErrorStatus, which stamps errorType")]
    public void NoProductionCode_CallsToStatusCodeDirectly()
    {
        var root = RepositoryRoot();
        var allowed = AllowedFiles.Select(relative => Path.GetFullPath(Path.Combine(root, relative))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path))
            .Where(path => !allowed.Contains(Path.GetFullPath(path)))
            .Where(path => File.ReadAllText(path).Contains(BypassCall, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "every one of these sends the right status and no errorType — replace `EndpointHelper.ToStatusCode(x)` " +
            "with `this.ErrorStatus(x)` (or `endpoint.ErrorStatus(x)` in an extension), which maps to the same " +
            "status and also stamps the type for TypedErrorResponse. See docs/claude/endpoints.md, 'Error bodies'");
    }

    /// <summary>The guard would be vacuous if the wrapper it points people at stopped calling the mapping.</summary>
    [Fact(DisplayName = "The ErrorStatus wrapper is the one place that still calls the mapping")]
    public void TheWrapper_StillCallsTheMapping()
    {
        var wrapper = Path.Combine(RepositoryRoot(), AllowedFiles[1]);

        File.ReadAllText(wrapper).Should().Contain(BypassCall,
            "ErrorStatus is documented as 'ToStatusCode plus the stamp'; if it no longer delegates, the allow-list above is stale");
    }

    /// <summary>
    /// The second bypass, which the text scan above cannot see: a failed <c>Result</c> answered with a
    /// hand-picked status through the bare FastEndpoints <c>Send.ErrorsAsync(int, ct)</c>. That call has
    /// no parameter for the type, so the type the <c>Result</c> carried is simply dropped — the 2FA login's
    /// lockout 401 went out that way (B25) and no status-asserting test could tell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A syntax walk rather than a regex, because the shape is structural: inside the <c>if (x.Failed)</c>
    /// branch (or the <c>else</c> of <c>if (!x.Failed)</c>), every <c>Send.ErrorsAsync(...)</c> must be one
    /// of the two typed forms — <c>Send.ErrorsAsync(this.ErrorStatus(type), ct)</c> when the status is the
    /// mapped one, or <c>Send.ErrorsAsync(type, status, [lockoutEnd,] ct)</c> when it is hand-picked. A
    /// literal-first or status-first call in that branch is the drop.
    /// </para>
    /// <para>
    /// Deliberately scoped to <c>.Failed</c> branches: a literal 404 after a null check has no type in hand
    /// to drop, and typing those is a different (and much larger) change. Widen this when that lands.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "No endpoint answers a failed Result with a bare Send.ErrorsAsync — the type it carries must reach the body")]
    public void NoFailedResultBranch_SendsErrorsWithoutTheType()
    {
        var root = RepositoryRoot();

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path))
            .SelectMany(path => TypelessSendsInFailedBranches(path)
                .Select(line => $"{Path.GetRelativePath(root, path)}:{line}"))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "each of these has a failed Result in hand and sends its message without the type — use " +
            "`Send.ErrorsAsync(result.ErrorType, <status>, ct)` to keep the literal status, or " +
            "`Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct)` for the mapped one. " +
            "See docs/claude/endpoints.md, 'Error bodies'");
    }

    /// <summary>Line numbers (1-based) of every typeless <c>Send.ErrorsAsync</c> in a <c>.Failed</c> branch of the file.</summary>
    private static IEnumerable<int> TypelessSendsInFailedBranches(string path)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);

        foreach (var ifStatement in tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>())
        {
            var branch = FailedBranch(ifStatement);
            if (branch is null)
                continue;

            foreach (var send in branch.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (send.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "ErrorsAsync", Expression: IdentifierNameSyntax { Identifier.Text: "Send" } })
                    continue;
                if (IsTypedSend(send))
                    continue;

                yield return send.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
        }
    }

    /// <summary>
    /// The statements that run when the result failed: the <c>then</c> of <c>if (x.Failed)</c>, the
    /// <c>else</c> of <c>if (!x.Failed)</c>, or null when the condition is about something else.
    /// </summary>
    private static StatementSyntax? FailedBranch(IfStatementSyntax ifStatement)
    {
        var condition = ifStatement.Condition;
        var negated = false;
        while (true)
        {
            switch (condition)
            {
                case ParenthesizedExpressionSyntax paren:
                    condition = paren.Expression;
                    continue;
                case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not:
                    negated = !negated;
                    condition = not.Operand;
                    continue;
            }

            break;
        }

        if (condition is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Failed" })
            return null;

        return negated ? ifStatement.Else?.Statement : ifStatement.Statement;
    }

    /// <summary>
    /// The two forms that carry a type: the first argument is <c>ErrorStatus(...)</c> (mapped status,
    /// stamped inside), or the call is the typed overload — a type first and the status second.
    /// </summary>
    private static bool IsTypedSend(InvocationExpressionSyntax send)
    {
        var args = send.ArgumentList.Arguments;
        if (args.Count == 0)
            return false;

        var first = args[0].Expression;
        if (first is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ErrorStatus" } or IdentifierNameSyntax { Identifier.Text: "ErrorStatus" } })
            return true;

        // Typed overload: (ResultErrorType? type, int status, ...). Without a semantic model the
        // tell is the second argument being the status — a literal or a *StatusCode member — while
        // the first is not a literal.
        if (args.Count < 2 || first is LiteralExpressionSyntax)
            return false;

        var second = args[1].Expression;
        return second is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NumericLiteralExpression }
               || second is MemberAccessExpressionSyntax { Name.Identifier.Text: "StatusCode" }
               || second is IdentifierNameSyntax { Identifier.Text: "statusCode" or "status" };
    }

    private static bool IsExcluded(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => ExcludedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding the solution file, which works from both
    /// <c>bin/Debug</c> and the relocated <c>artifacts-cli</c> tree. Same shape as <c>SliceDependencyTests</c>.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdhdTimeOrganizer.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("this guard scans the source tree, so it needs the repository root above {0}", AppContext.BaseDirectory);

        return directory!.FullName;
    }
}
