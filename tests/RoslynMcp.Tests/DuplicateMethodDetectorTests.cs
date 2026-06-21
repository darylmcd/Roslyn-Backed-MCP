using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="DuplicateMethodDetectorService"/>. Uses an in-memory
/// <see cref="AdhocWorkspace"/> for fast, isolated runs — the detector works off
/// syntax trees, so no MSBuildWorkspace is required.
/// </summary>
[TestClass]
public sealed class DuplicateMethodDetectorTests
{
    private const string WorkspaceId = "dup-test-ws";

    [TestMethod]
    public async Task FindDuplicatedMethods_StructurallyIdenticalBodies_Cluster()
    {
        // Two methods with identical structure: different names, different local names,
        // different formatting — the canonical form collapses all of that to the same key.
        var source = """
            namespace Sample;
            public class Helper
            {
                public int AddOne(int value)
                {
                    var temp = value + 1;
                    var result = temp * 2;
                    var doubled = result + result;
                    var halved = doubled / 2;
                    var finalValue = halved - 1;
                    return finalValue;
                }

                public int Alternate(int input)
                {
                    var x = input + 1;
                    var y = x * 2;
                    var z = y + y;
                    var w = z / 2;
                    var v = w - 1;
                    return v;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count, "Expected one cluster for the two structurally identical methods.");
        Assert.AreEqual(2, groups[0].MemberCount);
        Assert.AreEqual(1.0, groups[0].Similarity);
        CollectionAssert.AreEquivalent(
            new[] { "AddOne", "Alternate" },
            groups[0].Methods.Select(m => m.MethodName).ToArray());
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_UnrelatedMethods_DoNotCluster()
    {
        // Three structurally distinct methods — no two should collapse to the same canonical form.
        var source = """
            namespace Sample;
            public class Unrelated
            {
                public string Describe(string input)
                {
                    if (input is null) return "null";
                    var length = input.Length;
                    var trimmed = input.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    return $"{upper}:{length}";
                }

                public int Compute(int a, int b)
                {
                    var sum = a + b;
                    for (var i = 0; i < a; i++)
                    {
                        sum += i;
                    }
                    return sum;
                }

                public bool Validate(object payload)
                {
                    if (payload is null) throw new System.ArgumentNullException(nameof(payload));
                    var type = payload.GetType();
                    var name = type.FullName;
                    return name is { Length: > 0 };
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 5, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Structurally distinct methods must not cluster. Got: " +
            string.Join(", ", groups.SelectMany(g => g.Methods.Select(m => m.MethodName))));
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_McpToolWrappers_AreExcludedByDefault()
    {
        var source = """
            namespace ModelContextProtocol.Server
            {
                public sealed class McpServerToolAttribute : System.Attribute
                {
                    public string? Name { get; set; }
                }
            }

            namespace Sample
            {
                using ModelContextProtocol.Server;

                public interface IToolSink
                {
                    string Dispatch(string workspaceId);
                }

                public static class ToolWrappers
                {
                    [McpServerTool(Name = "tool_a")]
                    public static string ToolA(IToolSink sink, string workspaceId)
                    {
                        return sink.Dispatch(workspaceId);
                    }

                    [McpServerTool(Name = "tool_b")]
                    public static string ToolB(IToolSink sink, string workspaceId)
                    {
                        return sink.Dispatch(workspaceId);
                    }

                    [McpServerTool(Name = "tool_c")]
                    public static string ToolC(IToolSink sink, string workspaceId)
                    {
                        return sink.Dispatch(workspaceId);
                    }
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 1, Limit = 50 },
            default);

        Assert.IsFalse(
            groups.SelectMany(g => g.Methods).Any(m => m.ContainingType == "ToolWrappers"),
            "MCP tool wrapper shims are intentionally repetitive and should be excluded by default.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_McpToolWrappers_CanBeIncluded()
    {
        var source = """
            namespace ModelContextProtocol.Server
            {
                public sealed class McpServerToolAttribute : System.Attribute
                {
                    public string? Name { get; set; }
                }
            }

            namespace Sample
            {
                using ModelContextProtocol.Server;

                public interface IToolSink
                {
                    string Dispatch(string workspaceId);
                }

                public static class ToolWrappers
                {
                    [McpServerTool(Name = "tool_a")]
                    public static string ToolA(IToolSink sink, string workspaceId)
                    {
                        return sink.Dispatch(workspaceId);
                    }

                    [McpServerTool(Name = "tool_b")]
                    public static string ToolB(IToolSink sink, string workspaceId)
                    {
                        return sink.Dispatch(workspaceId);
                    }
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions
            {
                MinLines = 1,
                Limit = 50,
                ExcludeMcpToolWrappers = false
            },
            default);

        Assert.IsTrue(
            groups.SelectMany(g => g.Methods).Any(m => m.ContainingType == "ToolWrappers"),
            "Callers can opt back into wrapper clusters when they explicitly want raw structural duplicates.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_OverloadsWithIdenticalBodies_Cluster()
    {
        // Overload-handling invariant: bucketing is by body shape, not method name —
        // so two overloads that happen to share an identical body DO cluster.
        var source = """
            namespace Sample;
            public class Overloads
            {
                public int Process(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }

                public int Process(long value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return (int)tally;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        // The two bodies differ only at the final return expression: one is `return tally;`,
        // the other is `return (int)tally;`. That's a structural difference (an extra cast
        // node) — so they should NOT cluster. This verifies the inverse of the above
        // invariant: overloads with genuinely-different bodies do not collide.
        Assert.AreEqual(0, groups.Count,
            "Overloads with different bodies must not cluster.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_OverloadsWithTrulyIdenticalBodies_Cluster()
    {
        // Same name, same body shape (including the final return) — should cluster.
        var source = """
            namespace Sample;
            public class Overloads
            {
                public int Process(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }

                public int Process(int value, int unused)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count, "Overloads with identical bodies must cluster.");
        Assert.AreEqual(2, groups[0].MemberCount);
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ShortBodies_AreIgnored()
    {
        // Bodies below MinLines produce too many false-positive clusters (one-liners like
        // trivial forwarders). Default minLines=10 silently skips them.
        var source = """
            namespace Sample;
            public class ShortHelpers
            {
                public int Add(int a) => a + 1;
                public int Bump(int a) => a + 1;
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 10, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Expression-bodied one-liners should be filtered out by the default MinLines.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_LiteralValuesDistinguishGroups()
    {
        // Two methods whose structure is identical except for the literal values they
        // return. The canonical form preserves literals so they should NOT cluster — two
        // helpers returning "admin" vs "user" role strings are semantically different.
        var source = """
            namespace Sample;
            public class RoleHelpers
            {
                public string Admin()
                {
                    var label = "admin";
                    var prefix = label + "-";
                    var suffix = prefix + "role";
                    var full = suffix.ToLowerInvariant();
                    var trimmed = full.Trim();
                    return trimmed;
                }

                public string User()
                {
                    var label = "user";
                    var prefix = label + "-";
                    var suffix = prefix + "role";
                    var full = suffix.ToLowerInvariant();
                    var trimmed = full.Trim();
                    return trimmed;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Bodies differing only on literal values should NOT cluster — preserves literal semantics.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_GeneratedFiles_AreExcluded()
    {
        // A method in a .g.cs file must be ignored even if it structurally matches a
        // user-authored duplicate. Autogens are outside the target audience.
        var normalSource = """
            namespace Sample;
            public class Hand
            {
                public int Do(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b + b;
                    var d = c / 2;
                    var e = d - 1;
                    return e;
                }
            }
            """;
        var generatedSource = """
            namespace Sample;
            public class Auto
            {
                public int Do(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b + b;
                    var d = c / 2;
                    var e = d - 1;
                    return e;
                }
            }
            """;

        var service = BuildServiceWithSources(
            ("Hand.cs", normalSource),
            ("Auto.g.cs", generatedSource));

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            ".g.cs files must be excluded from duplicate-method detection.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_AbstractAndInterfaceMembers_AreSkipped()
    {
        // Abstract declarations and interface method headers have no body — they must
        // never be considered for bucketing (a bucket of empty strings would cluster
        // every abstract method together and be useless).
        var source = """
            namespace Sample;
            public interface IShape
            {
                int Area();
                int Perimeter();
            }
            public abstract class Base
            {
                public abstract int Area();
                public abstract int Perimeter();
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 1, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "Abstract methods and interface declarations have no body — must not cluster.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_SymmetricMapperPair_IsDownrankedAndTagged()
    {
        // Two methods with identical bodies whose names form a strict To*/From* complementary
        // pair (ToDto/FromDto). The bodies are by-design symmetric for the same data shape,
        // so clustering them as "duplicates" is a false positive. The detector should still
        // emit the cluster (so it remains visible) but downrank similarity and tag it with
        // ClusterKind = "round-trip-mapper" so threshold-based callers can skip it.
        var source = """
            namespace Sample;
            public sealed record UserDto(string Name, int Age);
            public sealed class User
            {
                public string Name { get; set; } = string.Empty;
                public int Age { get; set; }

                public UserDto ToDto(User user)
                {
                    var name = user.Name;
                    var age = user.Age;
                    var dto = new UserDto(name, age);
                    var verified = dto;
                    var copy = verified;
                    return copy;
                }

                public UserDto FromDto(User user)
                {
                    var name = user.Name;
                    var age = user.Age;
                    var dto = new UserDto(name, age);
                    var verified = dto;
                    var copy = verified;
                    return copy;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count, "Mapper pair should still be emitted (just downranked).");
        Assert.AreEqual(2, groups[0].MemberCount);
        Assert.AreEqual("round-trip-mapper", groups[0].ClusterKind,
            "Symmetric To*/From* pair must be tagged as round-trip-mapper.");
        Assert.IsTrue(groups[0].Similarity < 1.0,
            $"Similarity must be downranked from 1.0 for mapper pairs. Got {groups[0].Similarity}.");
        CollectionAssert.AreEquivalent(
            new[] { "ToDto", "FromDto" },
            groups[0].Methods.Select(m => m.MethodName).ToArray());
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_TheoryMethods_AreExcluded()
    {
        // xUnit [Theory] test methods share an identical dispatch shape (assert against every
        // [InlineData] row) and would otherwise bucket together. The detector must filter them
        // out by attribute-name suffix regardless of whether the source uses the bare [Theory]
        // form or the fully-qualified [TheoryAttribute] form. Pure syntactic check — no xUnit
        // reference is required for the test source (the attribute names just need to parse).
        var source = """
            namespace Sample;

            public sealed class TheoryAttribute : System.Attribute { }
            public sealed class InlineDataAttribute : System.Attribute
            {
                public InlineDataAttribute(params object[] data) { _ = data; }
            }

            public class TheoryFixtures
            {
                [Theory]
                [InlineData(1, 2)]
                [InlineData(3, 4)]
                public void AssertSumBareTheory(int a, int b)
                {
                    var sum = a + b;
                    var inverted = -sum;
                    var doubled = inverted * 2;
                    var halved = doubled / 2;
                    var final = halved + 0;
                    System.Diagnostics.Debug.Assert(final == -sum);
                }

                [TheoryAttribute]
                [InlineData(5, 6)]
                public void AssertSumFullName(int a, int b)
                {
                    var sum = a + b;
                    var inverted = -sum;
                    var doubled = inverted * 2;
                    var halved = doubled / 2;
                    var final = halved + 0;
                    System.Diagnostics.Debug.Assert(final == -sum);
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(0, groups.Count,
            "[Theory]-annotated methods must be excluded from clustering (both bare and full-name forms).");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ToFromWithThreeMembers_IsNotDownranked()
    {
        // The mapper-pair classifier is intentionally strict: only exactly-two members trip
        // it. A bucket of three (e.g. ToDto + FromDto + a coincidental third helper with the
        // same body shape) is structural duplication that happens to include a mapper — not
        // a designed symmetric pair — and should NOT be downranked.
        var source = """
            namespace Sample;
            public sealed record UserDto(string Name, int Age);
            public sealed class User
            {
                public string Name { get; set; } = string.Empty;
                public int Age { get; set; }

                public UserDto ToDto(User user)
                {
                    var name = user.Name;
                    var age = user.Age;
                    var dto = new UserDto(name, age);
                    var verified = dto;
                    var copy = verified;
                    return copy;
                }

                public UserDto FromDto(User user)
                {
                    var name = user.Name;
                    var age = user.Age;
                    var dto = new UserDto(name, age);
                    var verified = dto;
                    var copy = verified;
                    return copy;
                }

                public UserDto Snapshot(User user)
                {
                    var name = user.Name;
                    var age = user.Age;
                    var dto = new UserDto(name, age);
                    var verified = dto;
                    var copy = verified;
                    return copy;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(3, groups[0].MemberCount);
        Assert.IsNull(groups[0].ClusterKind,
            "Three-member bucket must not be tagged as round-trip-mapper.");
        Assert.AreEqual(1.0, groups[0].Similarity,
            "Three-member bucket retains full similarity (not a designed pair).");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ToFromWithMismatchedStems_IsNotDownranked()
    {
        // ToFoo and FromBar share an AST shape coincidentally — the stems don't match, so the
        // structural collision is genuine duplication, not a designed mapper pair. The
        // classifier must reject the downrank.
        var source = """
            namespace Sample;
            public sealed class Helpers
            {
                public int ToFoo(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }

                public int FromBar(int value)
                {
                    var tally = value;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    tally += 1;
                    return tally;
                }
            }
            """;

        var service = BuildServiceWithSource(source, out _);

        var groups = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].MemberCount);
        Assert.IsNull(groups[0].ClusterKind,
            "Mismatched stems (ToFoo/FromBar) must not be tagged as round-trip-mapper.");
        Assert.AreEqual(1.0, groups[0].Similarity,
            "Mismatched-stem bucket retains full similarity.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ProjectFilter_ScopesScanToProject()
    {
        // Load two projects: the structurally-matching methods sit in different projects.
        // When `ProjectFilter` targets only one project, cross-project clustering
        // disappears because only one member ends up in the bucket.
        var (service, adhoc) = CreateAdhocServiceWithTwoProjects();
        _ = adhoc;

        var filtered = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50, ProjectFilter = "ProjectA" },
            default);

        Assert.AreEqual(0, filtered.Count,
            "ProjectFilter should scope the scan so cross-project duplicates disappear.");

        var unfiltered = await service.FindDuplicatedMethodsAsync(
            WorkspaceId,
            new DuplicateMethodAnalysisOptions { MinLines = 6, Limit = 50 },
            default);

        Assert.AreEqual(1, unfiltered.Count,
            "Without a filter, the cross-project duplicate should still cluster.");
    }

    /// <summary>
    /// Source with three independent duplicate clusters (six methods, two per cluster). Each
    /// cluster has a distinct body shape so they bucket separately; the two members within a
    /// cluster are structurally identical (different names, different locals) so they cluster.
    /// Used by the summary-mode tests below.
    /// </summary>
    private const string ThreeClusterSource = """
        namespace Sample;
        public class ThreeClusters
        {
            // Cluster 1: arithmetic chain.
            public int ArithA(int value)
            {
                var a1 = value + 1;
                var a2 = a1 * 2;
                var a3 = a2 + a2;
                var a4 = a3 / 2;
                var a5 = a4 - 1;
                return a5;
            }
            public int ArithB(int input)
            {
                var b1 = input + 1;
                var b2 = b1 * 2;
                var b3 = b2 + b2;
                var b4 = b3 / 2;
                var b5 = b4 - 1;
                return b5;
            }

            // Cluster 2: loop-accumulate.
            public int LoopA(int count)
            {
                var total = 0;
                for (var i = 0; i < count; i++)
                {
                    total += i;
                    total -= 1;
                }
                return total;
            }
            public int LoopB(int n)
            {
                var acc = 0;
                for (var j = 0; j < n; j++)
                {
                    acc += j;
                    acc -= 1;
                }
                return acc;
            }

            // Cluster 3: string transform.
            public string StrA(string text)
            {
                var s1 = text.Trim();
                var s2 = s1.ToUpperInvariant();
                var s3 = s2 + "!";
                var s4 = s3.Replace("!", "?");
                var s5 = s4.PadLeft(10);
                return s5;
            }
            public string StrB(string raw)
            {
                var t1 = raw.Trim();
                var t2 = t1.ToUpperInvariant();
                var t3 = t2 + "!";
                var t4 = t3.Replace("!", "?");
                var t5 = t4.PadLeft(10);
                return t5;
            }
        }
        """;

    [TestMethod]
    public async Task FindDuplicatedMethods_SummaryMode_OmitsMethodsArrayButKeepsClusterMetadata()
    {
        // Drive the tool wrapper (not the service) so the summary-mode serialization branch in
        // FindDuplicatedMethodsCore is exercised. The tool runs through the real
        // WorkspaceExecutionGate against the AdhocWorkspace-backed TestWorkspaceManager.
        var (service, gate) = BuildServiceAndGate(ThreeClusterSource);
        using var gateScope = gate;

        var summaryJson = await AdvancedAnalysisTools.FindDuplicatedMethods(
            gate,
            service,
            workspaceId: WorkspaceId,
            minLines: 6,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            excludeMcpToolWrappers: true,
            summary: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(summaryJson);
        var root = doc.RootElement;

        Assert.AreEqual(3, root.GetProperty("count").GetInt32(),
            "All three clusters must be counted in summary mode.");
        Assert.IsTrue(root.GetProperty("summary").GetBoolean(),
            "Summary mode must stamp summary=true.");
        Assert.IsTrue(root.TryGetProperty("deprecation", out var deprecation));
        Assert.AreEqual(JsonValueKind.Null, deprecation.ValueKind,
            "Canonical tool must publish deprecation=null even in summary mode.");

        var groups = root.GetProperty("groups");
        Assert.AreEqual(3, groups.GetArrayLength(), "Each cluster must still appear as a group.");

        foreach (var group in groups.EnumerateArray())
        {
            // (a) The per-member Methods array is omitted — that's the byte budget the summary buys.
            Assert.IsFalse(group.TryGetProperty("methods", out _),
                "Summary group must omit the per-member `methods` array.");
            Assert.IsFalse(group.TryGetProperty("Methods", out _),
                "Summary group must omit the per-member `Methods` array (any casing).");

            // (b) The cluster metadata is preserved.
            Assert.IsTrue(group.TryGetProperty("normalizedHash", out var hash));
            Assert.IsFalse(string.IsNullOrEmpty(hash.GetString()),
                "normalizedHash must survive into the summary shape.");
            Assert.IsTrue(group.TryGetProperty("memberCount", out var memberCount));
            Assert.IsTrue(memberCount.GetInt32() >= 2,
                "Every emitted cluster has at least two members.");
            Assert.IsTrue(group.TryGetProperty("lineCount", out var lineCount));
            Assert.IsTrue(lineCount.GetInt32() > 0, "lineCount must survive into the summary shape.");
            Assert.IsTrue(group.TryGetProperty("similarity", out _),
                "similarity must survive into the summary shape.");
            Assert.IsTrue(group.TryGetProperty("clusterKind", out _),
                "clusterKind must survive into the summary shape.");
        }
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_FullMode_RetainsMethodsArray()
    {
        // The inverse contract: default (summary=false) preserves the per-member Methods array
        // and does NOT stamp a `summary` field. Pins that summary mode is strictly opt-in.
        var (service, gate) = BuildServiceAndGate(ThreeClusterSource);
        using var gateScope = gate;

        var fullJson = await AdvancedAnalysisTools.FindDuplicatedMethods(
            gate,
            service,
            workspaceId: WorkspaceId,
            minLines: 6,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            excludeMcpToolWrappers: true,
            summary: false,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(fullJson);
        var root = doc.RootElement;

        Assert.AreEqual(3, root.GetProperty("count").GetInt32());
        Assert.IsFalse(root.TryGetProperty("summary", out _),
            "Full mode must NOT emit a `summary` field.");

        var groups = root.GetProperty("groups");
        Assert.AreEqual(3, groups.GetArrayLength());
        foreach (var group in groups.EnumerateArray())
        {
            Assert.IsTrue(group.TryGetProperty("methods", out var methods),
                "Full mode must retain the per-member `methods` array.");
            Assert.IsTrue(methods.GetArrayLength() >= 2,
                "Each full-mode cluster lists all its member locations.");
        }
    }

    /// <summary>
    /// Builds the detector service AND a <see cref="WorkspaceExecutionGate"/> sharing one
    /// <see cref="TestWorkspaceManager"/> so the tool wrapper (which routes through the gate)
    /// can be exercised end-to-end. Default <see cref="ExecutionGateOptions"/> are fine: the
    /// test manager reports <c>IsStale=false</c> and <c>ContainsWorkspace=true</c>, so the
    /// staleness/reload path never runs and the gate resolves to a plain read.
    /// </summary>
    private static (DuplicateMethodDetectorService service, WorkspaceExecutionGate gate) BuildServiceAndGate(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "TestProject.csproj")));

        var docId = DocumentId.CreateNewId(projectId);
        var fullPath = Path.Combine(Path.GetTempPath(), "Sample.cs");
        workspace.AddDocument(DocumentInfo.Create(
            docId,
            "Sample.cs",
            filePath: fullPath,
            loader: TextLoader.From(
                TextAndVersion.Create(
                    SourceText.From(source),
                    VersionStamp.Create(),
                    fullPath))));

        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        var service = new DuplicateMethodDetectorService(wsManager);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), wsManager);
        return (service, gate);
    }

    /// <summary>
    /// Builds a service backed by an <see cref="AdhocWorkspace"/> containing a single
    /// document with the given source. Returns the configured service; out-parameter
    /// exposes the underlying workspace for tests that need it.
    /// </summary>
    private static DuplicateMethodDetectorService BuildServiceWithSource(string source, out AdhocWorkspace workspace)
    {
        return BuildServiceWithSourcesCore(out workspace, ("Sample.cs", source));
    }

    private static DuplicateMethodDetectorService BuildServiceWithSources(params (string fileName, string source)[] docs)
    {
        return BuildServiceWithSourcesCore(out _, docs);
    }

    private static DuplicateMethodDetectorService BuildServiceWithSourcesCore(out AdhocWorkspace workspace, params (string fileName, string source)[] docs)
    {
        workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), "TestProject.csproj"));
        workspace.AddProject(projectInfo);

        foreach (var (fileName, source) in docs)
        {
            var docId = DocumentId.CreateNewId(projectId);
            var fullPath = Path.Combine(Path.GetTempPath(), fileName);
            workspace.AddDocument(DocumentInfo.Create(
                docId,
                fileName,
                filePath: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(
                        SourceText.From(source),
                        VersionStamp.Create(),
                        fullPath))));
        }

        var wsManager = new TestWorkspaceManager(WorkspaceId, workspace);
        return new DuplicateMethodDetectorService(wsManager);
    }

    /// <summary>
    /// Build an AdhocWorkspace with two projects so we can exercise
    /// <see cref="DuplicateMethodAnalysisOptions.ProjectFilter"/> behaviour.
    /// </summary>
    private static (DuplicateMethodDetectorService service, AdhocWorkspace workspace) CreateAdhocServiceWithTwoProjects()
    {
        const string bodySource = """
            public int Compute(int value)
            {
                var tally = value;
                tally += 1;
                tally += 1;
                tally += 1;
                tally += 1;
                tally += 1;
                return tally;
            }
            """;

        var adhoc = new AdhocWorkspace();
        foreach (var projectName in new[] { "ProjectA", "ProjectB" })
        {
            var projectId = ProjectId.CreateNewId();
            adhoc.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name: projectName,
                assemblyName: projectName,
                language: LanguageNames.CSharp,
                filePath: Path.Combine(Path.GetTempPath(), $"{projectName}.csproj")));

            var docId = DocumentId.CreateNewId(projectId);
            var fileName = $"{projectName}.cs";
            var fullPath = Path.Combine(Path.GetTempPath(), fileName);
            var source = $"namespace {projectName};\npublic class Helper\n{{\n    {bodySource}\n}}\n";
            adhoc.AddDocument(DocumentInfo.Create(
                docId,
                fileName,
                filePath: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(
                        SourceText.From(source),
                        VersionStamp.Create(),
                        fullPath))));
        }

        var wsManager = new TestWorkspaceManager(WorkspaceId, adhoc);
        var service = new DuplicateMethodDetectorService(wsManager);
        return (service, adhoc);
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in for the detector tests. Only
    /// <see cref="GetCurrentSolution"/> and the two version hooks the compilation cache
    /// calls are wired; the rest throw to surface accidental coupling.
    /// </summary>
    private sealed class TestWorkspaceManager : IWorkspaceManager
    {
        private readonly string _workspaceId;
        private readonly AdhocWorkspace _workspace;

        public event Action<string>? WorkspaceClosed;
        public event Action<string>? WorkspaceReloaded;

        public TestWorkspaceManager(string workspaceId, AdhocWorkspace workspace)
        {
            _workspaceId = workspaceId;
            _workspace = workspace;
        }

        public void RaiseWorkspaceClosed(string workspaceId) => WorkspaceClosed?.Invoke(workspaceId);
        public void RaiseWorkspaceReloaded(string workspaceId) => WorkspaceReloaded?.Invoke(workspaceId);

        public Solution GetCurrentSolution(string workspaceId)
        {
            return workspaceId == _workspaceId
                ? _workspace.CurrentSolution
                : throw new InvalidOperationException($"Unknown workspace {workspaceId}");
        }

        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public bool ContainsWorkspace(string workspaceId) => workspaceId == _workspaceId;
        public bool IsStale(string workspaceId) => false;
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
    }
}
