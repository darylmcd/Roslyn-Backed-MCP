using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// How a tool's advertised output schema is produced from the DTO type its
/// <see cref="McpServerToolAttribute.OutputSchemaType"/> names.
/// </summary>
internal enum OutputSchemaKind
{
    /// <summary>
    /// One response shape: the advertised schema is the declared DTO's generated schema verbatim.
    /// </summary>
    Fixed,

    /// <summary>
    /// A mode-dependent response: the advertised schema is a variant union whose first branch is
    /// the declared DTO and whose remaining branches are the other modes the tool can serialize.
    /// </summary>
    Union,
}

/// <summary>
/// The JSON-Schema combinators a <see cref="OutputSchemaKind.Union"/> declaration may advertise.
/// A closed set rather than a free-form keyword string: an unrecognised combinator would emit an
/// invalid schema that no round-trip assertion could see, because every reader would look the
/// misspelled key straight back up.
/// </summary>
internal enum OutputSchemaUnionCombinator
{
    /// <summary>
    /// JSON-Schema <c>anyOf</c>: a response may satisfy one or more branches. Correct when the
    /// branches overlap, so demanding exactly one match would reject a valid response.
    /// </summary>
    AnyOf,

    /// <summary>
    /// JSON-Schema <c>oneOf</c>: a response satisfies exactly one branch. Correct when the modes
    /// are mutually exclusive by shape.
    /// </summary>
    OneOf,
}

/// <summary>
/// Explicit, per-tool statement of how <see cref="ToolOutputSchemaIndex"/> builds one advertised
/// output schema. Declaring the route (rather than inferring it from a fall-through
/// <see langword="switch"/>) is what makes the index the single generation authority: a tool with
/// no declaration, or a declaration with no tool, aborts index construction instead of silently
/// falling back to a generated shape nobody reviewed.
/// <para>
/// Construction is factory-only (<see cref="FixedShape"/> / <see cref="UnionOf"/>) so a
/// declaration cannot be assembled in an invalid state — in particular, a union can only ever
/// name a combinator from <see cref="OutputSchemaUnionCombinator"/>.
/// </para>
/// </summary>
internal sealed record OutputSchemaDeclaration
{
    private OutputSchemaDeclaration(
        OutputSchemaKind kind,
        OutputSchemaUnionCombinator? combinator,
        IReadOnlyList<Type> additionalVariants)
    {
        Kind = kind;
        Combinator = combinator;
        AdditionalVariants = additionalVariants;
    }

    /// <summary>Fixed DTO shape or mode-dependent union.</summary>
    internal OutputSchemaKind Kind { get; }

    /// <summary>
    /// JSON-Schema combinator for <see cref="OutputSchemaKind.Union"/> declarations;
    /// <see langword="null"/> for a fixed shape.
    /// </summary>
    internal OutputSchemaUnionCombinator? Combinator { get; }

    /// <summary>
    /// Response DTOs beyond the SDK-declared one. Empty for a fixed shape.
    /// </summary>
    internal IReadOnlyList<Type> AdditionalVariants { get; }

    /// <summary>
    /// The literal JSON-Schema key this declaration writes (<c>anyOf</c> or <c>oneOf</c>), or
    /// <see langword="null"/> for a fixed shape. Derived from <see cref="Combinator"/> so the
    /// advertised keyword has exactly one spelling authority.
    /// </summary>
    internal string? UnionKeyword => Combinator switch
    {
        null => null,
        OutputSchemaUnionCombinator.AnyOf => "anyOf",
        OutputSchemaUnionCombinator.OneOf => "oneOf",
        _ => throw new InvalidOperationException(
            $"No JSON-Schema keyword is defined for combinator '{Combinator}'."),
    };

    /// <summary>
    /// Declares that the tool advertises its SDK-declared DTO shape unchanged.
    /// </summary>
    internal static OutputSchemaDeclaration FixedShape() =>
        new(OutputSchemaKind.Fixed, combinator: null, additionalVariants: []);

    /// <summary>
    /// Declares that the tool serializes more than one DTO depending on its request mode, and
    /// advertises all of them under <paramref name="combinator"/>.
    /// </summary>
    internal static OutputSchemaDeclaration UnionOf(
        OutputSchemaUnionCombinator combinator,
        params Type[] additionalVariants)
    {
        // An enum is not a closed set at runtime — any int can be cast into one — so reject an
        // undefined value here rather than let it reach the advertised schema as a bad keyword.
        if (!Enum.IsDefined(combinator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(combinator),
                combinator,
                "A union declaration must name a defined JSON-Schema combinator (anyOf or oneOf).");
        }

        ArgumentNullException.ThrowIfNull(additionalVariants);
        if (additionalVariants.Length == 0)
        {
            throw new ArgumentException(
                "A union declaration must name at least one response variant beyond the SDK-declared DTO.",
                nameof(additionalVariants));
        }

        if (Array.IndexOf(additionalVariants, null) >= 0)
        {
            throw new ArgumentException(
                "A union declaration cannot name a null response variant.",
                nameof(additionalVariants));
        }

        if (additionalVariants.Distinct().Count() != additionalVariants.Length)
        {
            throw new ArgumentException(
                "A union declaration cannot name the same response variant twice.",
                nameof(additionalVariants));
        }

        // Copy defensively: params binds the caller's array when it is passed explicitly, so
        // storing it by reference would let a caller mutate a validated declaration afterwards.
        return new(OutputSchemaKind.Union, combinator, [.. additionalVariants]);
    }

    /// <summary>
    /// Ordered response DTOs for this declaration: the SDK-declared type first, then the extra
    /// modes. A fixed declaration yields exactly <paramref name="declaredDtoType"/>.
    /// </summary>
    internal IReadOnlyList<Type> Variants(Type declaredDtoType)
    {
        ArgumentNullException.ThrowIfNull(declaredDtoType);
        return Kind == OutputSchemaKind.Fixed
            ? [declaredDtoType]
            : [declaredDtoType, .. AdditionalVariants];
    }
}

/// <summary>
/// tool-output-schema-infrastructure: the single generation authority for every advertised
/// <c>outputSchema</c>. Reflection over each <see cref="McpServerToolAttribute"/>-attributed method
/// that declares an <see cref="McpServerToolAttribute.OutputSchemaType"/> supplies the DTO type;
/// the <see cref="Declarations"/> table supplies the route (fixed shape or variant union); and
/// <see cref="System.Text.Json.Schema.JsonSchemaExporter"/> projects the result into JSON Schema.
/// <para>
/// The two halves must agree exactly. An SDK adopter with no declaration, or a declaration with no
/// SDK adopter, throws at index construction — the asymmetry that would otherwise let an advertised
/// schema drift from the shape the server actually serializes.
/// <see cref="SurfaceRegistrationPolicy"/> enforces the same symmetry against the SDK's own
/// generated schemas at registration time.
/// </para>
/// <para>
/// Schema generation clones <see cref="JsonDefaults.Indented"/> — the same options object the
/// runtime <c>structuredContent</c> channel serializes with — so the advertised property names,
/// enum spellings, and converter projections match the wire bytes. Letting the SDK's own generator
/// win instead would drop camelCase and the string-enum converter. Any future polymorphic contract
/// must be expressed in the DTO's serializer metadata so runtime output and the advertised schema
/// continue to share one type source.
/// </para>
/// <para>
/// Reflection and generation run once at first access; the dictionary is immutable thereafter —
/// schema generation per type is deterministic so the cache is safe across the server's lifetime.
/// Tools without an <see cref="McpServerToolAttribute.OutputSchemaType"/> are absent from the
/// dictionary and <see cref="GetSchema"/> returns <see langword="null"/>.
/// </para>
/// </summary>
internal static class ToolOutputSchemaIndex
{
    /// <summary>
    /// Schema-export options matched to the server's runtime serializer. The exporter recursively
    /// describes the nested records and collections used by current tool DTOs.
    /// </summary>
    private static readonly JsonSchemaExporterOptions _exportOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
    };

    // The options are already a COPY of JsonDefaults.Indented, so the exporter inherits the
    // runtime naming policy and converters. The resolver is constructed unconditionally rather
    // than read back from the shared static: JsonDefaults.Indented.TypeInfoResolver is null until
    // something freezes that process-wide object, which would make the exporter's resolver depend
    // on static-initialization order. Both branches produced an unmodified reflection resolver, so
    // this is order-independence at no cost in behavior.
    private static readonly JsonSerializerOptions _schemaSerializerOptions = new(JsonDefaults.Indented)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// The declared generation route per advertised tool. Every entry here MUST have a matching
    /// <c>[McpServerTool(OutputSchemaType = ...)]</c> method, and every such method MUST appear
    /// here; <see cref="BuildIndex"/> fails closed on either asymmetry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, OutputSchemaDeclaration> _declarations =
        new Dictionary<string, OutputSchemaDeclaration>(StringComparer.Ordinal)
        {
            ["server_info"] = OutputSchemaDeclaration.FixedShape(),
            ["server_heartbeat"] = OutputSchemaDeclaration.FixedShape(),
            ["workspace_health"] = OutputSchemaDeclaration.FixedShape(),
            ["workspace_drift_check"] = OutputSchemaDeclaration.FixedShape(),
            ["workspace_readiness_report"] = OutputSchemaDeclaration.FixedShape(),
            ["workspace_support_bundle"] = OutputSchemaDeclaration.FixedShape(),

            // Both workspace-list variants have the same outer object shape. In particular,
            // { count: 0, workspaces: [] } satisfies both item schemas because an empty array
            // has no element with which to distinguish summary from verbose. oneOf would
            // therefore reject a valid fresh-host response; anyOf expresses the real contract.
            ["workspace_list"] = OutputSchemaDeclaration.UnionOf(
                OutputSchemaUnionCombinator.AnyOf, typeof(WorkspaceListVerboseDto)),

            // workspace_status serializes exactly one of its two shapes per request mode and the
            // verbose shape carries fields the summary lacks, so oneOf is the precise contract.
            ["workspace_status"] = OutputSchemaDeclaration.UnionOf(
                OutputSchemaUnionCombinator.OneOf, typeof(WorkspaceStatusDto)),
        };

    private static readonly Lazy<IReadOnlyDictionary<string, Type>> _adopters =
        new(DiscoverAdopters, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyDictionary<string, JsonNode>> _index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The declared generation route per advertised tool. Exposed <see langword="internal"/> so the
    /// catalog contract tests can assert the fixed-versus-union matrix without re-deriving it.
    /// </summary>
    internal static IReadOnlyDictionary<string, OutputSchemaDeclaration> Declarations => _declarations;

    /// <summary>
    /// The DTO type each tool declares through <see cref="McpServerToolAttribute.OutputSchemaType"/>,
    /// discovered by reflection over the host assembly — i.e. exactly the set for which the MCP SDK
    /// generates its own <c>outputSchema</c>. Exposed <see langword="internal"/> so tests can
    /// compare the SDK-discovered surface against <see cref="Declarations"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, Type> SdkDeclaredOutputSchemaTypes => _adopters.Value;

    /// <summary>
    /// Returns the cached JSON-Schema node for <paramref name="toolName"/>, or
    /// <see langword="null"/> when the tool has no declared output schema (the legacy
    /// text-only contract; structuredContent stays absent).
    /// </summary>
    public static JsonNode? GetSchema(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return null;
        return _index.Value.TryGetValue(toolName, out var schema)
            // Return a deep clone so callers cannot mutate the cached node.
            ? schema.DeepClone()
            : null;
    }

    /// <summary>
    /// Returns the cached set of tool names that have an opted-in output schema. Used by tests
    /// to spot the surface count without re-running the schema generation pipeline.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredToolNames => _index.Value.Keys.ToArray();

    /// <summary>
    /// Generates a JSON-Schema node for the given CLR type using the server's runtime
    /// serializer options. Exposed <see langword="internal"/> so catalog contract tests can
    /// compare a declared union branch with the exact DTO schema generated by production.
    /// </summary>
    internal static JsonNode GenerateSchema(Type type) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(_schemaSerializerOptions, type, _exportOptions);

    private static IReadOnlyDictionary<string, Type> DiscoverAdopters()
    {
        // Anchor on a known tool-host type so we walk the same assembly that MCP discovery uses.
        var assembly = typeof(Tools.ServerTools).Assembly;
        var adopters = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr?.Name is null) continue;

                var schemaType = toolAttr.OutputSchemaType;
                if (schemaType is null) continue;

                adopters[toolAttr.Name] = schemaType;
            }
        }

        return adopters;
    }

    private static IReadOnlyDictionary<string, JsonNode> BuildIndex()
    {
        var adopters = _adopters.Value;

        // Fail closed on either asymmetry. An opted-in schema is part of the advertised public
        // contract: an undeclared route would publish whatever the exporter happened to produce,
        // and a stale declaration would advertise a shape no tool can return.
        foreach (var (toolName, schemaType) in adopters)
        {
            if (!_declarations.ContainsKey(toolName))
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' declares [McpServerTool(OutputSchemaType = typeof({schemaType.Name}))] " +
                    $"but {nameof(ToolOutputSchemaIndex)} has no output-schema declaration for it. Add an explicit " +
                    $"{nameof(OutputSchemaDeclaration)}.{nameof(OutputSchemaDeclaration.FixedShape)}() or " +
                    $".{nameof(OutputSchemaDeclaration.UnionOf)}(...) entry so exactly one authority produces the " +
                    "advertised schema.");
            }
        }

        foreach (var toolName in _declarations.Keys)
        {
            if (!adopters.ContainsKey(toolName))
            {
                throw new InvalidOperationException(
                    $"{nameof(ToolOutputSchemaIndex)} declares an output schema for '{toolName}' but no " +
                    "[McpServerTool(OutputSchemaType = ...)] method in the host assembly carries that name. " +
                    "Remove the stale declaration or restore the tool's OutputSchemaType.");
            }
        }

        var dict = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (toolName, schemaType) in adopters)
        {
            // Let export failures abort startup instead of silently degrading that tool to
            // text-only output while its metadata still claims structured-content support.
            dict[toolName] = BuildAdvertisedSchema(_declarations[toolName], schemaType);
        }

        return dict;
    }

    private static JsonNode BuildAdvertisedSchema(OutputSchemaDeclaration declaration, Type declaredDtoType)
    {
        var variants = declaration.Variants(declaredDtoType);
        if (declaration.Kind == OutputSchemaKind.Fixed)
        {
            return GenerateSchema(variants[0]);
        }

        return new JsonObject
        {
            ["type"] = "object",
            [declaration.UnionKeyword!] = new JsonArray(variants.Select(GenerateSchema).ToArray()),
        };
    }
}
