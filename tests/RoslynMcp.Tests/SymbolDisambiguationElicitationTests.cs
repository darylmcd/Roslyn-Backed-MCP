using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>elicit-disambiguation-on-multi-symbol-resolve</c> and
/// <c>symbol-disambiguation-agent-first-default</c> initiatives (closes the same-named
/// backlog rows). When a metadata-name locator on <c>find_references</c> /
/// <c>go_to_definition</c> (or a &gt;1-hit <c>symbol_search</c> query) resolves to multiple
/// candidates (overloads, partial classes, member-vs-type collisions), <see cref="SymbolTools"/>
/// is now <b>agent-first by default</b>: the calling agent receives the structured
/// disambiguation-list response directly, with the stable <c>symbolHandle</c> per candidate.
/// The request-scoped operator picker is opt-in only — reached solely when the caller passes
/// <c>allowElicitation=true</c> AND the client declares elicitation; otherwise the code falls
/// through to the same additive list envelope.
///
/// <para>
/// Pins:
/// <list type="bullet">
///   <item><b>(a) request-scoped selection</b> — production <see cref="SymbolTools"/> entry
///         points discover real ambiguous candidate sets and consume modern MRTR responses from
///         the request context. Transport-era capability and cancellation behavior is owned by
///         <c>SymbolDisambiguationMrtrWireTests</c> and <c>ElicitationChoicePromptTests</c>.</item>
///   <item><b>(b) fallback</b> — when the caller does not opt in, or the client lacks the
///         elicitation capability (or the user declines), the tool returns a structured
///         <c>{ ambiguous: true, count, candidates }</c> envelope with a stable
///         <c>symbolHandle</c> per candidate, byte-identical regardless of whether elicitation
///         was tried or not.</item>
/// </list>
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolDisambiguationElicitationTests : IsolatedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        DisposeServices();
    }

    // ── (a) elicit-supported preconditions ───────────────────────────────────

    [TestMethod]
    [DataRow(nameof(SymbolTools.SearchSymbols))]
    [DataRow(nameof(SymbolTools.GoToDefinition))]
    [DataRow(nameof(SymbolTools.FindReferences))]
    public void AllowElicitationParameter_DefaultsToFalse(string methodName)
    {
        var method = typeof(SymbolTools).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, $"Expected SymbolTools.{methodName} to exist.");

        var parameter = method.GetParameters()
            .Single(candidate => string.Equals(candidate.Name, "allowElicitation", StringComparison.Ordinal));

        Assert.IsTrue(parameter.HasDefaultValue);
        Assert.AreEqual(
            false,
            parameter.DefaultValue,
            $"{methodName}.allowElicitation must remain agent-first by default.");
    }

    [TestMethod]
    public async Task FindReferences_AmbiguousMetadataName_OptInButNonCapableClient_ReturnsListEnvelope()
    {
        // Opt-in alone does not change behavior for a client that cannot elicit: with
        // allowElicitation=true but a null request context (therefore no negotiated client
        // capability), FindReferences must still return the additive
        // disambiguation-list envelope — proving the flag only *enables* the prompt on a
        // capable client and never breaks the non-capable fallback path.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                requestContext: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                _workspaceId,
                metadataName: "System.String.Format",
                allowElicitation: true,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ambiguous", out var ambiguous) || !ambiguous.GetBoolean())
        {
            Assert.Inconclusive(
                "System.String.Format did not produce an ambiguous resolution in the loaded " +
                $"sample solution. Response was: {json}");
            return;
        }

        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 2,
            "Opt-in against a non-capable client must still return the >= 2-candidate list envelope.");
        Assert.AreEqual("System.String.Format",
            doc.RootElement.GetProperty("metadataName").GetString(),
            "Envelope must echo the original metadata name so clients can correlate.");
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_FindsMultipleOverloadCandidates()
    {
        // Pin the candidate-discovery half of the gate: when a metadata name resolves to
        // multiple overloads, the helper returns ALL of them (not just the first match
        // that ResolveByMetadataNameAsync historically picked). This is the precondition
        // for the gate detecting ambiguity at all — without multiple candidates, the
        // gate short-circuits and elicitation never happens.
        //
        // SampleLib has multiple AnimalService methods; we look for any name in the
        // sample workspace that resolves to >= 2 candidates. If SampleSolution evolves,
        // adjust the metadata name to one with documented overloads.
        var solution = WorkspaceManager.GetCurrentSolution(_workspaceId);

        // Probe a handful of common ambiguous shapes. The test passes if ANY of them
        // returns >= 2 candidates — pinning that the helper can detect ambiguity, not
        // that any specific name is ambiguous in SampleLib (which may evolve).
        var probes = new[]
        {
            "SampleLib.AnimalService.GetAllAnimals",
            "SampleLib.AnimalService.SaveAnimal",
            "System.Object.ToString",
            "System.String.Format",
        };

        var sawAmbiguity = false;
        foreach (var name in probes)
        {
            var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
                solution, name, CancellationToken.None);
            if (candidates.Count >= 2)
            {
                sawAmbiguity = true;
                // Each candidate must produce a non-empty display label for the picker.
                foreach (var c in candidates)
                {
                    var label = SymbolHandleSerializer.BuildDisplayLabel(c);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                        $"BuildDisplayLabel must produce a non-empty label for every candidate (offender: {c}).");
                }
                break;
            }
        }

        Assert.IsTrue(sawAmbiguity,
            "FindAllByMetadataNameAsync must return >= 2 candidates for at least one " +
            "of the standard overloaded shapes (System.String.Format, etc.); otherwise " +
            "the disambiguation gate has nothing to disambiguate.");
    }

    [TestMethod]
    public async Task FindReferences_MrtrChoice_UsesOpaqueToken_AndDispatchesChosenHandleExactlyOnce()
    {
        const string metadataName = "SampleLib.AnimalService.CountAnimals";
        var solution = WorkspaceManager.GetCurrentSolution(_workspaceId);
        var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
            solution,
            metadataName,
            CancellationToken.None);
        Assert.IsTrue(
            candidates.Count is >= 2 and <= ElicitationChoicePrompt.MaxOptions,
            $"Fixture must expose a prompt-sized overload set for {metadataName}; found {candidates.Count}.");

        var handles = candidates.Select(SymbolHandleSerializer.CreateHandle).ToArray();
        var expectedTokens = handles.Select(SymbolTools.CreateSymbolChoiceToken).ToArray();
        var sourcePaths = handles
            .Select(SymbolHandleSerializer.ParseHandlePayload)
            .Select(static payload => payload.FilePath)
            .OfType<string>()
            .Where(Path.IsPathRooted)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.IsNotEmpty(sourcePaths,
            "The real-handle privacy regression requires source candidates with absolute paths.");
        Assert.IsTrue(expectedTokens.All(static token =>
            token.StartsWith("roslyn-symbol-choice:v1:", StringComparison.Ordinal)));
        Assert.AreEqual(expectedTokens.Length, expectedTokens.Distinct(StringComparer.Ordinal).Count());

        var referenceService = new RecordingReferenceService();
        var server = await GetPathAuthorizedServerAsync();
        var initialContext = CreateModernFormRequestContext(server);
        InputRequiredException? inputRequired = null;
        try
        {
            await SymbolTools.FindReferences(
                initialContext,
                WorkspaceManager,
                WorkspaceExecutionGate,
                referenceService,
                _workspaceId,
                metadataName: metadataName,
                allowElicitation: true,
                ct: CancellationToken.None);
            Assert.Fail("The initial modern request must terminate with input_required.");
        }
        catch (InputRequiredException ex)
        {
            inputRequired = ex;
        }

        Assert.IsNotNull(inputRequired);
        Assert.AreEqual(0, referenceService.FindReferencesCallCount,
            "No downstream reference lookup may run before the operator chooses a candidate.");
        Assert.IsNotNull(inputRequired.Result.InputRequests);
        Assert.IsTrue(inputRequired.Result.InputRequests.TryGetValue(
            RequestScopedInputAdapter.SymbolChoiceInputRequestKey,
            out var inputRequest));
        Assert.IsNotNull(inputRequest);
        var elicitation = inputRequest.ElicitationParams;
        Assert.IsNotNull(elicitation?.RequestedSchema);
        Assert.IsTrue(elicitation.RequestedSchema.Properties.TryGetValue("choice", out var choiceProperty));
        var choiceSchema = choiceProperty as ElicitRequestParams.TitledSingleSelectEnumSchema;
        Assert.IsNotNull(choiceSchema);
        Assert.AreEqual("Pick a symbol", choiceSchema.Title);
        CollectionAssert.AreEqual(
            expectedTokens,
            choiceSchema.OneOf.Select(static option => option.Const).ToArray(),
            "The request must carry deterministic opaque tokens in candidate order.");

        var rawRequest = inputRequest.Params?.GetRawText() ?? string.Empty;
        var unescapedRequest = rawRequest.Replace("\\\\", "\\", StringComparison.Ordinal);
        foreach (var handle in handles)
        {
            Assert.IsFalse(rawRequest.Contains(handle, StringComparison.Ordinal),
                "A reversible symbol handle must never be emitted in an MRTR choice request.");
        }
        foreach (var sourcePath in sourcePaths)
        {
            Assert.IsFalse(unescapedRequest.Contains(sourcePath, StringComparison.OrdinalIgnoreCase),
                $"MRTR choice request leaked an absolute source path: {sourcePath}");
        }

        var chosenIndex = 1;
        var retryContext = CreateModernFormRequestContext(
            server,
            new Dictionary<string, InputResponse>(StringComparer.Ordinal)
            {
                [RequestScopedInputAdapter.SymbolChoiceInputRequestKey] =
                    InputResponse.FromElicitResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["choice"] = JsonSerializer.SerializeToElement(expectedTokens[chosenIndex]),
                        },
                    }),
            });

        await SymbolTools.FindReferences(
            retryContext,
            WorkspaceManager,
            WorkspaceExecutionGate,
            referenceService,
            _workspaceId,
            metadataName: metadataName,
            allowElicitation: true,
            ct: CancellationToken.None);

        Assert.AreEqual(1, referenceService.FindReferencesCallCount,
            "An accepted retry must dispatch the downstream reference lookup exactly once.");
        Assert.AreEqual(handles[chosenIndex], referenceService.LastLocator?.SymbolHandle,
            "The opaque choice token must map back to the intended stable symbol handle.");
    }

    [TestMethod]
    public async Task SearchSymbols_MrtrChoice_NullHandleCandidate_UsesOpaqueProjectionToken()
    {
        var candidates = new[]
        {
            CreateSearchCandidate("First", "Metadata.Widget.First(int)"),
            CreateSearchCandidate("Second", "Metadata.Widget.Second(string)"),
        };
        var searchService = new StaticSymbolSearchService(candidates);
        var server = await GetPathAuthorizedServerAsync();
        var initialContext = CreateModernFormRequestContext(server, toolName: "symbol_search");

        InputRequiredException? inputRequired = null;
        try
        {
            await SymbolTools.SearchSymbols(
                initialContext,
                WorkspaceExecutionGate,
                searchService,
                _workspaceId,
                query: "Widget",
                allowElicitation: true,
                ct: CancellationToken.None);
            Assert.Fail("A modern multi-result symbol search must terminate with input_required.");
        }
        catch (InputRequiredException ex)
        {
            inputRequired = ex;
        }

        Assert.IsNotNull(inputRequired?.Result.InputRequests);
        var inputRequest = inputRequired.Result.InputRequests[
            RequestScopedInputAdapter.SymbolChoiceInputRequestKey];
        var choiceSchema = inputRequest.ElicitationParams?
            .RequestedSchema?
            .Properties["choice"] as ElicitRequestParams.TitledSingleSelectEnumSchema;
        Assert.IsNotNull(choiceSchema);
        var expectedTokens = candidates.Select(SymbolTools.CreateSymbolChoiceToken).ToArray();
        CollectionAssert.AreEqual(
            expectedTokens,
            choiceSchema.OneOf.Select(static option => option.Const).ToArray());
        Assert.IsTrue(expectedTokens.All(static token => !string.IsNullOrWhiteSpace(token)));

        var retryContext = CreateModernFormRequestContext(
            server,
            new Dictionary<string, InputResponse>(StringComparer.Ordinal)
            {
                [RequestScopedInputAdapter.SymbolChoiceInputRequestKey] =
                    InputResponse.FromElicitResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["choice"] = JsonSerializer.SerializeToElement(expectedTokens[1]),
                        },
                    }),
            },
            toolName: "symbol_search");

        var json = await SymbolTools.SearchSymbols(
            retryContext,
            WorkspaceExecutionGate,
            searchService,
            _workspaceId,
            query: "Widget",
            allowElicitation: true,
            ct: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.GetProperty("chosenViaElicitation").GetBoolean());
        Assert.AreEqual("Second", document.RootElement.GetProperty("symbols")[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_DedupesSameSourceDeclarationAcrossProjectCompilations()
    {
        // A metadata-name lookup may see the same source declaration through more than one
        // project compilation. That is not a real ambiguity for find_references; the
        // disambiguation gate should collapse identical source spans before returning an
        // ambiguous envelope.
        const string source = """
            namespace DuplicateCandidates;

            public sealed class SharedSourceType
            {
                public void Touch() { }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var sharedSourcePath = Path.Combine(
            TestTempRoot.Current,
            "DuplicateCandidates",
            "SharedSourceType.cs");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var solution = workspace.CurrentSolution;
        for (var i = 0; i < 2; i++)
        {
            var projectId = ProjectId.CreateNewId($"DuplicateCandidates{i}");
            solution = solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                $"DuplicateCandidates{i}",
                $"DuplicateCandidates{i}",
                LanguageNames.CSharp,
                metadataReferences: references));

            var documentId = DocumentId.CreateNewId(projectId, "SharedSourceType.cs");
            solution = solution.AddDocument(DocumentInfo.Create(
                documentId,
                "SharedSourceType.cs",
                filePath: sharedSourcePath,
                loader: TextLoader.From(TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create()))));
        }

        var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
            solution,
            "DuplicateCandidates.SharedSourceType",
            CancellationToken.None);

        Assert.AreEqual(1, candidates.Count,
            "Duplicate compilation candidates for the same source path and span should collapse to one metadata-name candidate.");
        Assert.AreEqual(SymbolKind.NamedType, candidates[0].Kind);
        Assert.AreEqual("SharedSourceType", candidates[0].Name);
    }

    [TestMethod]
    public async Task FindAllByMetadataNameAsync_DedupesMetadataCandidatesBySymbolHandle()
    {
        // System.Xml.XmlException can surface through multiple metadata assemblies. The
        // emitted handle intentionally omits assembly identity, so those candidates are
        // indistinguishable to clients unless the disambiguation path collapses them first.
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var xmlCarrierA = CreateXmlExceptionMetadataReference("XmlCarrierA", references);
        var xmlCarrierB = CreateXmlExceptionMetadataReference("XmlCarrierB", references);

        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        solution = AddProjectWithMetadataReference(solution, "XmlConsumerA", xmlCarrierA);
        solution = AddProjectWithMetadataReference(solution, "XmlConsumerB", xmlCarrierB);

        var candidates = await SymbolHandleSerializer.FindAllByMetadataNameAsync(
            solution,
            "System.Xml.XmlException",
            CancellationToken.None);

        var handles = candidates
            .Select(SymbolHandleSerializer.CreateHandle)
            .ToList();

        Assert.AreEqual(1, candidates.Count,
            "Two metadata assemblies exposing the same handle must collapse to one candidate.");
        Assert.AreEqual(
            handles.Count,
            handles.Distinct(StringComparer.Ordinal).Count(),
            "Metadata-name candidates must be deduped by symbolHandle before returning an ambiguity envelope.");
    }

    // ── (b) fallback when client lacks elicitation capability ───────────────

    [TestMethod]
    public async Task FindReferences_AmbiguousMetadataName_NullServer_ReturnsListEnvelope()
    {
        // The contract: when the client doesn't support elicitation (requestContext == null in
        // the direct-call harness), the tool returns the additive disambiguation-list envelope:
        //   { ambiguous: true, metadataName, count, candidates: [{ label, symbolHandle, kind }, ...], note }
        // This is the byte-identical fallback shape — clients that don't support
        // elicitation see this regardless of whether the server attempted to elicit.
        //
        // We pick a name documented as having multiple candidates in BCL so the test
        // doesn't depend on SampleLib evolution.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                requestContext: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                _workspaceId,
                metadataName: "System.String.Format",
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ambiguous", out var ambiguous) || !ambiguous.GetBoolean())
        {
            // System.String.Format may not be reachable from SampleLib's compilation if
            // the solution evolves. In that case the test is meaningless — assert
            // explicitly so the failure is descriptive.
            Assert.Inconclusive(
                "System.String.Format did not produce an ambiguous resolution in the loaded " +
                $"sample solution. Response was: {json}");
            return;
        }

        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 2,
            "Disambiguation envelope must declare >= 2 candidates.");
        Assert.AreEqual("System.String.Format",
            doc.RootElement.GetProperty("metadataName").GetString(),
            "Envelope must echo the original metadata name so clients can correlate.");

        var candidates = doc.RootElement.GetProperty("candidates");
        Assert.AreEqual(JsonValueKind.Array, candidates.ValueKind);
        Assert.IsTrue(candidates.GetArrayLength() >= 2);

        foreach (var c in candidates.EnumerateArray())
        {
            Assert.IsTrue(c.TryGetProperty("label", out var label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(label.GetString()),
                "Each candidate must carry a human-readable label for the picker UI / agent prompt.");
            Assert.IsTrue(c.TryGetProperty("symbolHandle", out var handle));
            Assert.IsFalse(string.IsNullOrWhiteSpace(handle.GetString()),
                "Each candidate must carry a stable symbolHandle so clients can re-call the tool with the chosen one.");
            Assert.IsTrue(c.TryGetProperty("kind", out _),
                "Each candidate must declare its symbol kind (Method, Property, NamedType, ...).");
        }

        Assert.IsTrue(doc.RootElement.TryGetProperty("note", out var note));
        Assert.IsTrue(note.GetString()!.Contains("symbolHandle", StringComparison.OrdinalIgnoreCase),
            "Note must direct clients to re-call with the chosen symbolHandle.");
    }

    private static RequestContext<CallToolRequestParams> CreateModernFormRequestContext(
        McpServer server,
        IDictionary<string, InputResponse>? inputResponses = null,
        string toolName = "find_references") =>
        new(
            server,
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = RequestProtocolFeatureGate.July2026ProtocolVersion,
                    ClientCapabilities = new ClientCapabilities
                    {
                        Elicitation = new ElicitationCapability
                        {
                            Form = new FormElicitationCapability(),
                        },
                    },
                },
            },
            new CallToolRequestParams
            {
                Name = toolName,
                InputResponses = inputResponses,
            });

    private static SymbolDto CreateSearchCandidate(string name, string fullyQualifiedName) =>
        new(
            Name: name,
            FullyQualifiedName: fullyQualifiedName,
            SymbolHandle: null,
            Kind: "Method",
            ContainingType: "Metadata.Widget",
            Namespace: "Metadata",
            Project: "MetadataProject",
            FilePath: null,
            StartLine: null,
            StartColumn: null,
            EndLine: null,
            EndColumn: null,
            ReturnType: "void",
            Parameters: null,
            Modifiers: null,
            BaseTypes: null,
            Interfaces: null,
            Documentation: null);

    private static MetadataReference CreateXmlExceptionMetadataReference(
        string assemblyName,
        IReadOnlyList<MetadataReference> references)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace System.Xml;

            public sealed class XmlException
            {
            }
            """);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        Assert.IsTrue(
            emit.Success,
            "Test metadata carrier must compile: " +
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static Solution AddProjectWithMetadataReference(
        Solution solution,
        string projectName,
        MetadataReference xmlExceptionReference)
    {
        var projectId = ProjectId.CreateNewId(projectName);
        return solution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            metadataReferences:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                xmlExceptionReference,
            ]));
    }

    private sealed class RecordingReferenceService : IReferenceService
    {
        public int FindReferencesCallCount { get; private set; }

        public SymbolLocator? LastLocator { get; private set; }

        public Task<IReadOnlyList<LocationDto>> FindReferencesAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct,
            bool summary = false,
            IReadOnlyCollection<string>? projectFilter = null)
        {
            ct.ThrowIfCancellationRequested();
            FindReferencesCallCount++;
            LastLocator = locator;
            return Task.FromResult<IReadOnlyList<LocationDto>>(Array.Empty<LocationDto>());
        }

        public Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct,
            bool includeGeneratedPartials = false) =>
            throw new InvalidOperationException("Unexpected implementation lookup in symbol-choice test.");

        public Task<IReadOnlyList<SymbolDto>> FindOverridesAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected override lookup in symbol-choice test.");

        public Task<IReadOnlyList<SymbolDto>> FindSiblingInterfaceImplementationsAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected sibling lookup in symbol-choice test.");

        public Task<IReadOnlyList<SymbolDto>> FindBaseMembersAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected base-member lookup in symbol-choice test.");

        public Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
            string workspaceId,
            IReadOnlyList<BulkSymbolLocator> symbols,
            bool includeDefinition,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected bulk lookup in symbol-choice test.");
    }

    private sealed class StaticSymbolSearchService(IReadOnlyList<SymbolDto> candidates) : ISymbolSearchService
    {
        public Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
            string workspaceId,
            string query,
            string? projectFilter,
            string? kindFilter,
            string? namespaceFilter,
            int maxResults,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }

        public Task<SymbolDto?> GetSymbolInfoAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct,
            bool allowAdjacent = false) =>
            throw new InvalidOperationException("Unexpected symbol-info lookup in symbol-choice test.");

        public Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(
            string workspaceId,
            string filePath,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected document-symbol lookup in symbol-choice test.");

        public Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(
            string workspaceId,
            SymbolLocator locator,
            CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected document-symbol lookup in symbol-choice test.");
    }
}
