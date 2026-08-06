using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Shared resolution rules for all four seeder managers: what gets resolved, in what order, and only
/// once each.
/// </summary>
internal static class SeederResolver
{
    /// <summary>
    /// All registered seeders of one kind, ascending by <c>Order</c>, at most one instance per
    /// concrete type.
    /// <para>
    /// The dedupe is not decorative. Hosts commonly run more than one assembly scan (one over
    /// <c>AppDomain.CurrentDomain.GetAssemblies()</c>, another over an explicit module list), and an
    /// assembly caught by both is registered twice against the same interface. <c>GetServices</c>
    /// then yields it twice and the seeder runs twice — duplicate fixture rows, or a unique-index
    /// violation. Keyed by concrete type rather than <c>SeederName</c> so two genuinely different
    /// seeders that share a name still both run (and collide loudly) instead of one silently winning.
    /// </para>
    /// </summary>
    internal static List<TSeeder> Resolve<TSeeder>(IServiceProvider serviceProvider, Assembly? assembly = null)
        where TSeeder : IDatabaseSeeder
    {
        var seeders = serviceProvider.GetServices<TSeeder>();

        if (assembly is not null)
            seeders = seeders.Where(s => s.GetType().Assembly == assembly);

        return seeders
            .GroupBy(s => s.GetType())
            .Select(g => g.First())
            .OrderBy(s => s.Order)
            .ToList();
    }

    /// <summary>
    /// The same set in reverse <c>Order</c> — truncation order. Seeders declare their FK dependencies
    /// once, through <c>Order</c>, and wiping walks it backwards so children go before parents.
    /// </summary>
    internal static List<TSeeder> ResolveForTruncation<TSeeder>(IServiceProvider serviceProvider, Assembly? assembly = null)
        where TSeeder : IDatabaseSeeder
    {
        var seeders = Resolve<TSeeder>(serviceProvider, assembly);
        seeders.Reverse();
        return seeders;
    }

    internal static TSeeder ResolveByName<TSeeder>(IServiceProvider serviceProvider, string seederName)
        where TSeeder : IDatabaseSeeder
    {
        var seeder = Resolve<TSeeder>(serviceProvider).FirstOrDefault(s => s.SeederName == seederName);

        return seeder ?? throw new InvalidOperationException(
            $"No {typeof(TSeeder).Name} with name '{seederName}' is registered.");
    }
}