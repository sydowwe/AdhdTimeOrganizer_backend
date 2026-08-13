using NJsonSchema;
using NJsonSchema.Generation;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.config.swagger;

/// <summary>
/// Strips <c>ToEntity</c> out of the generated schema of every request DTO implementing
/// <see cref="ICreateRequest{TEntity}"/>.
/// <para>
/// <c>ToEntity</c> is a get-only mapping property, not part of the request body, but NJsonSchema reflects
/// get-only properties like any other member — so it maps the raw EF entity behind it and drags the whole
/// navigation graph into the document. Several of those graphs are cyclic and blow the stack inside the
/// schema processors that walk the result.
/// </para>
/// <para>
/// A <c>[JsonIgnore]</c> on the interface member would not help: STJ and NJsonSchema read that attribute from
/// the concrete implementing property, not from the interface declaration, so it would have to be repeated on
/// every DTO. This processor is the single global place instead — registered in <c>Program.cs</c> via
/// <c>SchemaSettings.SchemaProcessors</c>.
/// </para>
/// <para>
/// <b>It must run before FastEndpoints' own <c>ValidationSchemaProcessor</c>, and that is not automatic.</b>
/// FastEndpoints registers its processor inside <c>EnableFastEndpoints</c> and only afterwards invokes the
/// host's <c>DocumentSettings</c> action, so registering this one with a plain <c>Add</c> places it *behind*
/// the validation processor — which then walks a schema that still carries <c>ToEntity</c>, descends into the
/// cyclic EF navigation graph and kills the process with a <see cref="StackOverflowException"/> on the first
/// request for <c>/swagger/v1/swagger.json</c>. That is not catchable, and it was diagnosed for a long time as
/// an upstream bug. <c>Program.cs</c> therefore rebuilds the collection to put this processor first; keep it
/// that way.
/// </para>
/// <para>
/// Note this runs <i>after</i> the type's schema is generated, so the entity schemas may still be left behind
/// in the document's definitions — unreferenced and harmless. What it guarantees is that no request body
/// advertises a <c>toEntity</c> field.
/// </para>
/// </summary>
public sealed class RemoveToEntitySchemaProcessor : ISchemaProcessor
{
    private const string ToEntityProperty = nameof(ICreateRequest<>.ToEntity);

    /// <summary>
    /// Registers a <see cref="RemoveToEntitySchemaProcessor"/> as the <b>first</b> processor in
    /// <paramref name="processors"/>, preserving the relative order of everything already there.
    /// <para>
    /// Call this instead of <c>processors.Add(new RemoveToEntitySchemaProcessor())</c> — see the type remarks
    /// for why running after FastEndpoints' <c>ValidationSchemaProcessor</c> overflows the stack. The
    /// collection is <see cref="ICollection{T}"/> (NSwag exposes no indexer), so prepending means rebuilding.
    /// </para>
    /// </summary>
    public static void PrependTo(ICollection<ISchemaProcessor> processors)
    {
        var existing = processors.ToList();
        processors.Clear();
        processors.Add(new RemoveToEntitySchemaProcessor());
        foreach (var processor in existing)
            processors.Add(processor);
    }

    public void Process(SchemaProcessorContext context)
    {
        if (!typeof(ICreateRequest).IsAssignableFrom(context.Type))
            return;

        // The property can sit either on the schema itself or, when the DTO inherits, on the allOf part
        // carrying its own members.
        RemoveToEntity(context.Schema);
        foreach (var schema in context.Schema.AllOf)
            RemoveToEntity(schema);
    }

    private static void RemoveToEntity(JsonSchema schema)
    {
        // Case-insensitive: the property name in the document follows the configured naming policy
        // (camelCase here, so "toEntity"), and that policy is not this processor's business.
        var names = schema.Properties.Keys
            .Where(name => string.Equals(name, ToEntityProperty, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var name in names)
        {
            schema.Properties.Remove(name);
            // Removing the property does not clear a "required" entry pointing at it, and a required
            // property with no definition makes the schema unsatisfiable for generated clients.
            schema.RequiredProperties.Remove(name);
        }
    }
}
