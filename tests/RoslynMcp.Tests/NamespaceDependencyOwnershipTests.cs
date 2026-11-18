using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NamespaceDependencyOwnershipTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task NamespaceGraph_AttributesTypesAndImportsToTheirScopes()
    {
        const string source = """
            using System;
            using Collections = System.Collections;

            public class GlobalType { }

            namespace ScopeFirst
            {
                using System.Text;
                using Text = System.Text;

                public class First
                {
                    public delegate void Callback();
                }

                namespace Inner
                {
                    using System.IO;

                    public enum Kind { Default }
                }
            }

            namespace ScopeSecond
            {
                using System.Threading;

                public record Second;
            }

            namespace ScopeSecond
            {
                public interface SecondContract { }
            }

            namespace ScopeEmpty { }
            """;
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ScopedDependencies.cs"), source);
        await workspace.LoadAsync();

        var result = await ReadGraphAsync(workspace.WorkspaceId, circularOnly: false);
        var owners = result.Nodes.Where(node => node.Namespace.StartsWith("Scope", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(
            new NamespaceNodeDto[]
            {
                new("ScopeEmpty", 0, "SampleLib"),
                new("ScopeFirst", 2, "SampleLib"),
                new("ScopeFirst.Inner", 1, "SampleLib"),
                new("ScopeSecond", 2, "SampleLib"),
            },
            owners);
        CollectionAssert.AreEqual(
            new NamespaceEdgeDto[]
            {
                new("ScopeFirst", "System", 1),
                new("ScopeFirst.Inner", "System", 1),
                new("ScopeSecond", "System", 1),
                new("ScopeEmpty", "System", 1),
                new("ScopeFirst", "System.Collections", 1),
                new("ScopeFirst.Inner", "System.Collections", 1),
                new("ScopeSecond", "System.Collections", 1),
                new("ScopeEmpty", "System.Collections", 1),
                new("ScopeFirst", "System.Text", 2),
                new("ScopeFirst.Inner", "System.IO", 1),
                new("ScopeSecond", "System.Threading", 1),
            },
            result.Edges.Where(edge => edge.FromNamespace.StartsWith("Scope", StringComparison.Ordinal)).ToArray());
        Assert.AreEqual(result.Nodes.Count(node => node.Project is not null), result.TotalNamespacesScanned);
        Assert.IsTrue(result.Nodes.Any(node => node.Namespace == "System.Collections" && node.Project is null));

        var circularOnly = await ReadGraphAsync(workspace.WorkspaceId, circularOnly: true);
        Assert.AreEqual(result.TotalNamespacesScanned, circularOnly.TotalNamespacesScanned);
        Assert.IsFalse(circularOnly.Nodes.Any(node => node.Namespace.StartsWith("Scope", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task NamespaceGraph_PreservesSingleFileScopedNamespace()
    {
        const string source = """
            using System.IO;

            namespace ScopeSingle;

            using System.Text;
            using Alias = System.IO;

            public delegate void Handler();
            public class Container { public struct Nested { } }
            """;
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "SingleScopeDependencies.cs"), source);
        await workspace.LoadAsync();

        var result = await ReadGraphAsync(workspace.WorkspaceId, circularOnly: false);
        Assert.AreEqual(new NamespaceNodeDto("ScopeSingle", 3, "SampleLib"),
            result.Nodes.Single(node => node.Namespace == "ScopeSingle"));
        CollectionAssert.AreEqual(
            new NamespaceEdgeDto[]
            {
                new("ScopeSingle", "System.IO", 2),
                new("ScopeSingle", "System.Text", 1),
            },
            result.Edges.Where(edge => edge.FromNamespace == "ScopeSingle").ToArray());
    }

    [TestMethod]
    public async Task NamespaceGraph_SkipsUnnamedRecoveryScopesAndIneligibleImports()
    {
        const string source = """
            global using System.Net;
            using static System.Math;
            using System.Text;

            namespace ScopeValid
            {
                public class Valid { }
            }

            namespace
            {
                public class Unnamed { }

                namespace ScopeMissingParent
                {
                    public class AlsoUnnamed { }
                }
            }

            namespace ScopeMissingSegment.
            {
                namespace ScopeMissingAncestor
                {
                    public class AlsoInvalid { }
                }
            }
            """;
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "RecoveryScopes.cs"), source);
        await workspace.LoadAsync();

        var result = await ReadGraphAsync(workspace.WorkspaceId, circularOnly: false);
        Assert.IsFalse(result.Nodes.Any(node =>
            node.Namespace.Length == 0 || node.Namespace.Contains("ScopeMissing", StringComparison.Ordinal)));
        Assert.AreEqual(new NamespaceNodeDto("ScopeValid", 1, "SampleLib"),
            result.Nodes.Single(node => node.Namespace == "ScopeValid"));
        CollectionAssert.AreEqual(
            new[] { new NamespaceEdgeDto("ScopeValid", "System.Text", 1) },
            result.Edges.Where(edge => edge.FromNamespace == "ScopeValid").ToArray());
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity copy site): an import edge was keyed by the
    /// syntax <c>Name.ToString()</c>, so an escaped or interior-trivia import of a declared
    /// namespace produced a phantom node and the real edge, and its cycle, went missing.
    /// </summary>
    [TestMethod]
    public async Task NamespaceGraph_KeysEdgesByNamespaceIdentityNotImportSpelling()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "SpellingA.cs"),
            "using @SpellingB;\n\nnamespace SpellingA\n{\n    public class AType { }\n}\n");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "SpellingB.cs"),
            "using Spelling . Hub ;\n\nnamespace SpellingB\n{\n    public class BType { }\n}\n");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "SpellingHub.cs"),
            "using SpellingA;\n\nnamespace Spelling.Hub\n{\n    public class HubType { }\n}\n");
        await workspace.LoadAsync();

        var result = await ReadGraphAsync(workspace.WorkspaceId, circularOnly: false);

        var spelled = result.Nodes.Where(node => node.Namespace.Contains('@') || node.Namespace.Contains(' ')).ToArray();
        Assert.AreEqual(0, spelled.Length, string.Join(", ", spelled.Select(node => node.Namespace)));
        CollectionAssert.Contains(result.Edges.Select(edge => (edge.FromNamespace, edge.ToNamespace)).ToArray(),
            ("SpellingA", "SpellingB"));
        CollectionAssert.Contains(result.Edges.Select(edge => (edge.FromNamespace, edge.ToNamespace)).ToArray(),
            ("SpellingB", "Spelling.Hub"));
        Assert.IsTrue(result.CircularDependencies.Any(cycle => cycle.Cycle.Contains("SpellingA") && cycle.Cycle.Contains("SpellingB")
            && cycle.Cycle.Contains("Spelling.Hub")), "The A -> B -> Hub -> A cycle must be reported.");
    }

    private static async Task<NamespaceDependencyGraphDto> ReadGraphAsync(string workspaceId, bool circularOnly)
    {
        var json = await AdvancedAnalysisTools.GetNamespaceDependencies(
            WorkspaceExecutionGate, NamespaceDependencyService, workspaceId, "SampleLib", circularOnly);
        return JsonSerializer.Deserialize<NamespaceDependencyGraphDto>(json, JsonDefaults.Indented)
            ?? throw new InvalidOperationException("Namespace graph response was null.");
    }
}
