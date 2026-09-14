using AdhdTimeOrganizer.application.dto.testSupport;
using AdhdTimeOrganizer.infrastructure.testSupport;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.testSupport;

/// <summary>
/// <c>POST /api/test/seed</c> — resets the database to the fixture the Playwright suite expects.
/// Called once by <c>e2e/global-setup.ts</c>, before any spec and before any login.
///
/// <para><b>The route only exists where <see cref="TestSeedGate"/> opened.</b> Everywhere else the
/// endpoint type is filtered out of FastEndpoints discovery in <c>Program.ConfigureServices</c>, so the
/// path 404s exactly as an undefined path does — deliberately not a 403, which would confirm the
/// endpoint is there. The <see cref="TestSeedGateDecision"/> check below is defence in depth for the
/// case where that registration filter is ever changed or bypassed; it is not the primary control.</para>
///
/// <para><b>Unauthenticated, and that is why the gate is what it is.</b> <c>globalSetup</c> runs with no
/// cookie and nothing to log in as — the accounts it needs are the ones this call creates. The
/// protection is therefore entirely "which process is this, and which database is it pointed at",
/// never "who is calling".</para>
///
/// <para><b>Synchronous.</b> It returns when the database is in the known state, not before. A
/// 202-and-poll shape would need a client change and <c>globalSetup</c> has nowhere to poll from.</para>
/// </summary>
public class SeedTestDataEndpoint(
    IE2eFixtureSeeder seeder,
    TestSeedGateDecision gate,
    ILogger<SeedTestDataEndpoint> logger) : EndpointWithoutRequest<TestSeedResponse>
{
    public override void Configure()
    {
        Post("/test/seed");

        // globalSetup has no cookie and no account — see the class remarks. AllowAnonymous also takes
        // this endpoint out of the DenyExtensionClients policy the global configurator attaches to
        // everything else, because AuthorizationMiddleware skips policy evaluation entirely for an
        // endpoint carrying IAllowAnonymous metadata.
        AllowAnonymous();

        Summary(s =>
        {
            s.Summary = "Resets the database to the known e2e fixture";
            s.Description =
                "Development-only, and only against a database on this machine. Truncates every table in " +
                "the model except the scheduler's, then rebuilds the roles, the root admin and the three " +
                "e2e accounts (two plain users and an admin) with their fixture data. Returns the stable " +
                "ids the specs reference.";
            s.Response<TestSeedResponse>(200, "The database is in the fixture state");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!gate.IsOpen)
        {
            // Unreachable while the discovery filter is in place. If it ever is reached, behave the way
            // an absent route does rather than admitting the endpoint exists.
            logger.LogError("The test-seed endpoint was reached with a closed gate ({Reason}); refusing.", gate.Reason);
            await Send.NotFoundAsync(ct);
            return;
        }

        // Deliberately no try/catch. A partial fixture is worse than no fixture: the 500 travels back
        // through globalSetup, which surfaces the status and body and fails the whole run — which is
        // what should happen. UnhandledExceptionMiddleware turns it into a 500 carrying an error id.
        var response = await seeder.ResetAsync(ct);

        await Send.OkAsync(response, ct);
    }
}
