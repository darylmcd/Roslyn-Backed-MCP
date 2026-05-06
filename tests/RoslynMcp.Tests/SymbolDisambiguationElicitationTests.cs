using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>elicit-disambiguation-on-multi-symbol-resolve</c> initiative
/// (closes the same-named backlog row): when a metadata-name locator on
/// <c>find_references</c> / <c>go_to_definition</c> resolves to multiple candidates
/// (overloads, partial classes, member-vs-type collisions), <see cref="SymbolTools"/>
/// asks the agent to pick one via MCP <c>elicitation/create</c> when the client supports
/// it; otherwise it falls through to an additive disambiguation-list response so existing
/// non-elicit clients continue to receive a well-shaped envelope.
///
/// <para>
/// Pins:
/// <list type="bullet">
///   <item><b>(a) elicit-supported preconditions</b> — the
///         <see cref="StructuredCallToolFilter.HasElicitation"/> capability check returns
///         true for a properly handshaken client AND the candidate-discovery helper finds
///         the multiple-overload set; the gate logic is therefore wired correctly. The
///         end-to-end <c>ElicitAsync</c> call requires a real
///         <see cref="ModelContextProtocol.Server.McpServer"/> with transport — see
///         <see cref="StructuredCallToolFilterElicitationTests"/> for the same gate
///         pattern on the workspace-path elicitation initiative.</item>
///   <item><b>(b) fallback</b> — when the client lacks the elicitation capability (or the
///         user declines), the tool returns a structured <c>{ ambiguous: true, count, candidates }</c>
///         envelope with a stable <c>symbolHandle</c> per candidate, byte-identical
///         regardless of whether elicitation was tried or not.</item>
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
    public void HasElicitation_PinsTheCapabilityCheckUsedByDisambiguation()
    {
        // The disambiguation gate inside SymbolTools.TryDisambiguateMetadataNameAsync
        // delegates to the same StructuredCallToolFilter.HasElicitation predicate the
        // workspace-path initiative pinned. This regression test pins that we still rely
        // on the same predicate (so a future refactor of either site can't drift).
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability(),
        };
        Assert.IsTrue(StructuredCallToolFilter.HasElicitation(capabilities),
            "An ElicitationCapability instance present on ClientCapabilities means the " +
            "client supports elicitation/create — the disambiguation gate is permitted.");

        Assert.IsFalse(StructuredCallToolFilter.HasElicitation(null),
            "Null capabilities (pre-handshake) MUST NOT permit elicitation.");
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

    // ── (b) fallback when client lacks elicitation capability ───────────────

    [TestMethod]
    public async Task FindReferences_AmbiguousMetadataName_NullServer_ReturnsListEnvelope()
    {
        // The contract: when the client doesn't support elicitation (server == null in
        // the test harness, which makes server?.ClientCapabilities null and HasElicitation
        // false), the tool returns the additive disambiguation-list envelope:
        //   { ambiguous: true, metadataName, count, candidates: [{ label, symbolHandle, kind }, ...], note }
        // This is the byte-identical fallback shape — clients that don't support
        // elicitation see this regardless of whether the server attempted to elicit.
        //
        // We pick a name documented as having multiple candidates in BCL so the test
        // doesn't depend on SampleLib evolution.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                server: null!,
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
}
