using Microsoft.Extensions.Logging;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// Captures every formatted log message across all categories, for asserting on content or presence.
/// <para>
/// The seeding suites are the only ones in the solution whose subject sometimes produces a log line
/// instead of a row — a skipped seeder returns early, a failed root-admin seed swallows the error —
/// so this is the only log-assertion harness here and all three of them share it. Register it as an
/// <see cref="ILoggerProvider"/> on a test host: <c>SerilogConfig</c> passes <c>writeToProviders: true</c>
/// to <c>UseSerilog</c>, so Serilog forwards every event to any provider in DI. That is the standard
/// Microsoft.Extensions.Logging seam, not a workaround for any one seeder.
/// </para>
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    /// <summary>A snapshot, taken under the same lock the writes take — seeders log from their own scopes.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
                return _messages.ToList();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
    public void Dispose() { }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages)
                messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
