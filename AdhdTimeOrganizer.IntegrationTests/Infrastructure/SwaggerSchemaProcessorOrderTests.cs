using AdhdTimeOrganizer.config.swagger;
using FluentAssertions;
using NJsonSchema.Generation;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the schema-processor ordering that keeps dev Swagger alive (CQ-33).
/// <para>
/// <see cref="RemoveToEntitySchemaProcessor"/> must run <b>before</b> FastEndpoints'
/// <c>ValidationSchemaProcessor</c>. FastEndpoints registers its own processor inside
/// <c>EnableFastEndpoints</c> and only afterwards invokes the host's <c>DocumentSettings</c> action, so
/// registering ours with a plain <c>Add</c> lands it behind the validation processor — which then walks a
/// schema still carrying <c>ICreateRequest&lt;TEntity&gt;.ToEntity</c>, descends into the cyclic EF navigation
/// graph behind it and kills the process with a <see cref="StackOverflowException"/> the first time
/// <c>/swagger/v1/swagger.json</c> is requested.
/// </para>
/// <para>
/// <b>Why this is a unit test and not a request against the document.</b> Two reasons. Swagger is registered
/// only when the environment is Development, which the test host is not — so there is no document to fetch.
/// And a <see cref="StackOverflowException"/> cannot be caught or contained: a test that regenerated the
/// document would, on regression, take the whole xunit run down with it and report nothing at all. Asserting
/// the invariant directly fails loudly with a readable message instead.
/// </para>
/// </summary>
public class SwaggerSchemaProcessorOrderTests
{
    /// <summary>
    /// The real registration shape: FastEndpoints has already added its processors by the time the host's
    /// <c>DocumentSettings</c> action runs, so ours has to overtake them rather than queue behind them.
    /// </summary>
    [Fact]
    public void PrependTo_PutsTheToEntityStripper_AheadOfProcessorsAlreadyRegistered()
    {
        ICollection<ISchemaProcessor> processors =
            new List<ISchemaProcessor> { new ValidationSchemaProcessor(), new PolymorphismSchemaProcessor() };

        RemoveToEntitySchemaProcessor.PrependTo(processors);

        processors.Should().HaveCount(3);
        processors.First().Should().BeOfType<RemoveToEntitySchemaProcessor>(
            "ToEntity has to be stripped before FastEndpoints' ValidationSchemaProcessor walks the schema");
    }

    /// <summary>
    /// Prepending rebuilds the collection (NSwag exposes it as an <see cref="ICollection{T}"/> with no
    /// indexer), so the rebuild must not disturb the processors FastEndpoints registered relative to
    /// each other — losing or reordering those would change the generated document in ways nothing else here
    /// would notice.
    /// </summary>
    [Fact]
    public void PrependTo_PreservesTheExistingProcessorsAndTheirRelativeOrder()
    {
        var validation = new ValidationSchemaProcessor();
        var polymorphism = new PolymorphismSchemaProcessor();
        ICollection<ISchemaProcessor> processors = new List<ISchemaProcessor> { validation, polymorphism };

        RemoveToEntitySchemaProcessor.PrependTo(processors);

        processors.Skip(1).Should().Equal(validation, polymorphism);
    }

    /// <summary>An empty collection is still a valid starting point — no special-casing.</summary>
    [Fact]
    public void PrependTo_HandlesAnEmptyCollection()
    {
        ICollection<ISchemaProcessor> processors = new List<ISchemaProcessor>();

        RemoveToEntitySchemaProcessor.PrependTo(processors);

        processors.Should().ContainSingle().Which.Should().BeOfType<RemoveToEntitySchemaProcessor>();
    }

    // Stand-ins for the processors FastEndpoints/NSwag register. Only their identity and position matter
    // here, so there is no reason to reach for the real (internal) FastEndpoints type.
    private sealed class ValidationSchemaProcessor : ISchemaProcessor
    {
        public void Process(SchemaProcessorContext context) { }
    }

    private sealed class PolymorphismSchemaProcessor : ISchemaProcessor
    {
        public void Process(SchemaProcessorContext context) { }
    }
}
