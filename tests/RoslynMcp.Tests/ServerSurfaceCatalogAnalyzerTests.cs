// Tests for the ServerSurfaceCatalogAnalyzer (RMCP001 / RMCP002). See
// analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs for the
// analyzer implementation; these tests exercise the three scenarios documented in
// ai_docs/plans/20260417T120000Z_backlog-sweep/plan.md initiative #2:
//
//   (a) attributed-but-missing-from-catalog  → expects RMCP001
//   (b) listed-but-not-attributed            → expects RMCP002
//   (c) all-matching                         → no diagnostics
//
// The runtime parity test (SurfaceCatalogTests.ServerSurfaceCatalog_CoversAllRegistered…)
// remains as a belt-and-braces assertion on the full server assembly; these tests
// isolate the analyzer itself against minimal, hand-written fixtures so regressions
// in its matching logic surface at the compile stage rather than at test-run.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Analyzers.ServerSurfaceCatalog;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServerSurfaceCatalogAnalyzerTests
{
    // Minimal stand-ins for the ModelContextProtocol.Server attribute shapes — only
    // the surface the analyzer actually matches on (the Name named-argument). Using
    // real MCP types would pull in package metadata the analyzer-testing harness has
    // no reason to load; this keeps the test fixture hermetic.
    //
    // The fixtures place these stubs LAST — after any `using` directives — because
    // C# forbids `using` to follow a type declaration (CS1529).
    private const string McpAttributeStubs = """

        namespace ModelContextProtocol.Server
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerToolAttribute : System.Attribute
            {
                public string? Name { get; set; }
                public bool ReadOnly { get; set; }
                public bool Destructive { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerResourceAttribute : System.Attribute
            {
                public string? Name { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerPromptAttribute : System.Attribute
            {
                public string? Name { get; set; }
            }
        }
        """;

    [TestMethod]
    public async Task NoDiagnostics_WhenCatalogAndAttributesAgreeAcrossAllKinds()
    {
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class ServerSurfaceCatalog
                {
                    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
                        new SurfaceEntry[]
                        {
                            Tool("alpha"),
                            Tool("beta"),
                        };

                    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
                        new SurfaceEntry[]
                        {
                            Resource("res_alpha"),
                        };

                    public static IReadOnlyList<SurfaceEntry> Prompts { get; } =
                        new SurfaceEntry[]
                        {
                            Prompt("prompt_alpha"),
                        };

                    private static SurfaceEntry Tool(string name) => new("tool", name);
                    private static SurfaceEntry Resource(string name) => new("resource", name);
                    private static SurfaceEntry Prompt(string name) => new("prompt", name);
                }
            }

            namespace RoslynMcp.Host.Stdio.Tools
            {
                public static class Host
                {
                    [McpServerTool(Name = "alpha")] public static void Alpha() { }
                    [McpServerTool(Name = "beta")] public static void Beta() { }
                    [McpServerResource(Name = "res_alpha")] public static void ResAlpha() { }
                    [McpServerPrompt(Name = "prompt_alpha")] public static void PromptAlpha() { }
                }
            }
            """ + McpAttributeStubs;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task RMCP001_WhenAttributedMethodHasNoCatalogEntry()
    {
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class ServerSurfaceCatalog
                {
                    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
                        new SurfaceEntry[]
                        {
                            Tool("alpha"),
                        };

                    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    public static IReadOnlyList<SurfaceEntry> Prompts { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    private static SurfaceEntry Tool(string name) => new("tool", name);
                }
            }

            namespace RoslynMcp.Host.Stdio.Tools
            {
                public static class Host
                {
                    [McpServerTool(Name = "alpha")] public static void Alpha() { }
                    [{|#0:McpServerTool(Name = "beta")|}] public static void Beta() { }
                }
            }
            """ + McpAttributeStubs;

        var expected = new DiagnosticResult("RMCP001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("beta", "McpServerTool", "Tools");

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task RMCP002_WhenCatalogEntryHasNoAttribute()
    {
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class ServerSurfaceCatalog
                {
                    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
                        new SurfaceEntry[]
                        {
                            Tool("alpha"),
                            Tool({|#0:"ghost"|}),
                        };

                    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    public static IReadOnlyList<SurfaceEntry> Prompts { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    private static SurfaceEntry Tool(string name) => new("tool", name);
                }
            }

            namespace RoslynMcp.Host.Stdio.Tools
            {
                public static class Host
                {
                    [McpServerTool(Name = "alpha")] public static void Alpha() { }
                }
            }
            """ + McpAttributeStubs;

        var expected = new DiagnosticResult("RMCP002", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ghost", "Tools", "Tool");

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task RMCP002_FiresPerKind_WhenResourceEntryIsOrphaned()
    {
        // Guards the kind-pairing arm: an orphan in Resources must say "McpServerResource",
        // not "McpServerTool", even when Tools is otherwise clean.
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class ServerSurfaceCatalog
                {
                    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
                        new SurfaceEntry[]
                        {
                            Resource({|#0:"orphan_res"|}),
                        };

                    public static IReadOnlyList<SurfaceEntry> Prompts { get; } =
                        System.Array.Empty<SurfaceEntry>();

                    private static SurfaceEntry Resource(string name) => new("resource", name);
                }
            }
            """ + McpAttributeStubs;

        var expected = new DiagnosticResult("RMCP002", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("orphan_res", "Resources", "Resource");

        await VerifyAsync(source, expected);
    }

    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ServerSurfaceCatalogAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
