// Exercises ServerSurfaceCatalog catalog invocations that live outside the Tools /
// Resources / Prompts property initializers (partial-class slice fields, auxiliary properties).
// See plan initiative catalog-split-phase1-relax-analyzer.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Analyzers.ServerSurfaceCatalog;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SliceFieldDetectionTests
{
    private const string McpAttributeStubs = """

        namespace ModelContextProtocol.Server
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerToolAttribute : System.Attribute
            {
                public string? Name { get; set; }
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
    public async Task NoDiagnostics_WhenToolFactoryLivesInSliceFieldArray()
    {
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class ServerSurfaceCatalog
                {
                    private static readonly SurfaceEntry[] WorkspaceTools =
                    [
                        Tool("alpha"),
                    ];

                    public static IReadOnlyList<SurfaceEntry> Tools { get; } = WorkspaceTools;

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

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task UnrelatedType_WithSameToolName_DoesNotPolluteCatalog()
    {
        var source = """
            using System.Collections.Generic;
            using ModelContextProtocol.Server;

            namespace RoslynMcp.Host.Stdio.Catalog
            {
                public sealed record SurfaceEntry(string Kind, string Name);

                public static class UnrelatedCatalog
                {
                    public static IReadOnlyList<SurfaceEntry> Items { get; } =
                        new SurfaceEntry[] { Tool("noise") };

                    private static SurfaceEntry Tool(string name) => new("tool", name);
                }

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
                }
            }
            """ + McpAttributeStubs;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task NoDiagnostics_WhenToolFactoryLivesInAuxiliaryNamedProperty()
    {
        // Proves kind is derived from the factory binding, not the declaring property's name.
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

                    public static IReadOnlyList<SurfaceEntry> ReservedTools { get; } =
                        new SurfaceEntry[]
                        {
                            Tool("beta"),
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
                    [McpServerTool(Name = "beta")] public static void Beta() { }
                }
            }
            """ + McpAttributeStubs;

        await VerifyAsync(source);
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
