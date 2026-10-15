using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit tests for <see cref="UnusedCodeAnalyzer.FindDeadFieldsAsync"/>. Uses an
/// in-memory <see cref="AdhocWorkspace"/> so the detector can classify source-declared
/// fields as never-read, never-written, or never-either without relying on the sample
/// solution's incidental shapes.
/// </summary>
[TestClass]
public sealed class DeadFieldDetectorTests
{
    private readonly List<UnusedCodeAnalyzerTestHarness> _harnesses = [];

    [TestCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await CleanupFailureCollector.RunAsync(
                "Failed to dispose analyzer fixtures.",
                _harnesses.Select(harness => CleanupFailureCollector.FromAction(harness.Dispose)).ToArray());
        }
        finally
        {
            _harnesses.Clear();
        }
    }

    private const string WorkspaceId = "dead-field-test-ws";

    [TestMethod]
    public async Task FindDeadFields_FieldWithInitializerAndNoReads_IsNeverRead()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count = 5;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-read", hits[0].UsageKind);
        Assert.AreEqual(0, hits[0].ReadReferenceCount);
        Assert.AreEqual(1, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldReadButNeverWritten_IsNeverWritten()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;
                public int Read() => _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-written", hits[0].UsageKind);
        Assert.AreEqual(1, hits[0].ReadReferenceCount);
        Assert.AreEqual(0, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldWithoutReferencesOrInitializer_IsNeverEither()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("_count", hits[0].SymbolName);
        Assert.AreEqual("never-either", hits[0].UsageKind);
        Assert.AreEqual(0, hits[0].ReadReferenceCount);
        Assert.AreEqual(0, hits[0].WriteReferenceCount);
    }

    [TestMethod]
    public async Task FindDeadFields_FieldWrittenAndRead_IsNotFlagged()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;

                public void Set(int value)
                {
                    _count = value;
                }

                public int Read() => _count;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public async Task FindDeadFields_RefArgumentCountsAsReadWrite()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;

                public void Touch()
                {
                    Bump(ref _count);
                }

                private static void Bump(ref int value)
                {
                    value++;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            "Passing a field by ref must count as both a read and a write.");
    }

    [TestMethod]
    public async Task FindDeadFields_DefaultExcludesPublicFields_AndUsageKindFilterWorks()
    {
        const string source = """
            namespace Sample;
            public sealed class Counter
            {
                public int PublicField = 1;
                private int _neverRead = 1;
                private int _neverWritten;

                public int Read() => _neverWritten;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var defaultHits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        CollectionAssert.AreEquivalent(
            new[] { "_neverRead", "_neverWritten" },
            defaultHits.Select(hit => hit.SymbolName).OrderBy(name => name).ToArray());

        var filteredHits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions
            {
                IncludePublic = true,
                UsageKindFilter = "never-read"
            },
            default);

        CollectionAssert.AreEquivalent(
            new[] { "PublicField", "_neverRead" },
            filteredHits.Select(hit => hit.SymbolName).OrderBy(name => name).ToArray());
        Assert.IsTrue(filteredHits.All(hit => hit.UsageKind == "never-read"));
    }

    [TestMethod]
    public async Task FindDeadFields_CtorAssignedButNeverRead_IsNotSafelyRemovable()
    {
        // The canonical DI-captured-field shape: the constructor assigns the field,
        // but nothing ever reads it. `find_dead_fields` correctly classifies it as
        // `never-read`, but `remove_dead_code_preview` will refuse ("still has
        // references") because the constructor assignment IS a source reference.
        // The envelope must surface that break in the chained workflow via
        // `removalBlockedBy` + `safelyRemovable=false` so callers don't retry.
        const string source = """
            namespace Sample;
            internal sealed class Consumer
            {
                private readonly string _options;

                public Consumer(string options)
                {
                    _options = options;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        var hit = hits[0];
        Assert.AreEqual("_options", hit.SymbolName);
        Assert.AreEqual("never-read", hit.UsageKind);
        Assert.AreEqual(0, hit.ReadReferenceCount);
        Assert.AreEqual(1, hit.WriteReferenceCount);
        Assert.IsFalse(hit.SafelyRemovable,
            "Ctor-written fields must be flagged as not safely removable because `remove_dead_code_preview` will refuse them.");
        Assert.IsNotNull(hit.RemovalBlockedBy);
        Assert.AreEqual(1, hit.RemovalBlockedBy!.Count);
        StringAssert.StartsWith(hit.RemovalBlockedBy[0], "ConstructorWrite@",
            "Blocker markers for constructor assignments must be tagged `ConstructorWrite@Path:Line:Col`.");
    }

    [TestMethod]
    public async Task FindDeadFields_InitializerOnlyNeverRead_IsSafelyRemovable()
    {
        // A field that only has a declaration initializer (no other refs) is safely
        // removable: `remove_dead_code_preview` erases the declaration which erases
        // the initializer in the same syntax edit. No residual references remain, so
        // `safelyRemovable=true` and `removalBlockedBy` is null.
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _count = 5;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(1, hits.Count);
        var hit = hits[0];
        Assert.AreEqual("_count", hit.SymbolName);
        Assert.AreEqual("never-read", hit.UsageKind);
        Assert.IsTrue(hit.SafelyRemovable,
            "A field with only a declaration initializer has no residual references and should be safely removable.");
        Assert.IsNull(hit.RemovalBlockedBy);
    }

    [TestMethod]
    public async Task FindDeadFields_StaticLazyFieldReadThroughValue_IsNotFlagged()
    {
        const string source = """
            using System;
            namespace Sample;
            internal static class CachedValue
            {
                private static readonly Lazy<int> s_value = new(() => 42);

                public static int Get() => s_value.Value;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.IsFalse(hits.Any(hit => hit.SymbolName == "s_value"),
            "A Lazy<T> field read through .Value must not be reported as dead.");
    }

    // Regression coverage for `find-dead-fields-compound-assignment-writes`:
    // every compound-assignment operator + `++`/`--` on both bare and member-access
    // field references must count as a write so the field is never classified as
    // `never-written`. `Interlocked.*` callsites that take the field by `ref` are
    // covered by the existing `RefKeyword` arm; adding the member-access case
    // here ensures `Interlocked.Increment(ref acc.field)` works for qualified refs.
    [TestMethod]
    [DataRow("_count++;", DisplayName = "PostfixIncrement")]
    [DataRow("_count--;", DisplayName = "PostfixDecrement")]
    [DataRow("++_count;", DisplayName = "PrefixIncrement")]
    [DataRow("--_count;", DisplayName = "PrefixDecrement")]
    [DataRow("_count += 1;", DisplayName = "AddAssign")]
    [DataRow("_count -= 1;", DisplayName = "SubtractAssign")]
    [DataRow("_count *= 2;", DisplayName = "MultiplyAssign")]
    [DataRow("_count /= 2;", DisplayName = "DivideAssign")]
    [DataRow("_count %= 2;", DisplayName = "ModuloAssign")]
    [DataRow("_count <<= 1;", DisplayName = "LeftShiftAssign")]
    [DataRow("_count >>= 1;", DisplayName = "RightShiftAssign")]
    [DataRow("_count |= 1;", DisplayName = "OrAssign")]
    [DataRow("_count &= 1;", DisplayName = "AndAssign")]
    [DataRow("_count ^= 1;", DisplayName = "XorAssign")]
    public async Task FindDeadFields_CompoundAssignmentCountsAsWrite(string statement)
    {
        var source = $$"""
            namespace Sample;
            internal sealed class Counter
            {
                private int _count;

                public int Touch()
                {
                    {{statement}}
                    return _count;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions(),
            default);

        Assert.AreEqual(0, hits.Count,
            $"Compound statement `{statement}` must register a write reference — otherwise the field is falsely classified as never-written.");
    }

    [TestMethod]
    public async Task FindDeadFields_MemberAccessCompoundAssignmentCountsAsWrite()
    {
        // `acc.ErrorCount++` from the diagnosis: the reference is the Name node of a
        // MemberAccessExpression. Prior to the fix, the classifier returned "Read"
        // because only the MemberAccess→Assignment arm matched, not the Postfix
        // case. This test locks in the unwrap that promotes MemberAccess to the
        // effective reference node before matching operator arms.
        const string source = """
            namespace Sample;
            internal sealed class Accumulator
            {
                public int ErrorCount;
            }
            internal sealed class Consumer
            {
                public int Touch(Accumulator acc)
                {
                    acc.ErrorCount++;
                    acc.ErrorCount += 2;
                    return acc.ErrorCount;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions { IncludePublic = true },
            default);

        var errorCount = hits.FirstOrDefault(hit => hit.SymbolName == "ErrorCount");
        Assert.IsNull(errorCount,
            "`acc.ErrorCount++` and `acc.ErrorCount += 2` must both count as writes on the field reference.");
    }

    [TestMethod]
    public async Task FindDeadFields_InterlockedIncrementCountsAsReadWrite()
    {
        // `Interlocked.Increment(ref acc.ErrorCount)` — the `ref` arg carries a
        // MemberAccess to the field. The existing `RefKeyword` arm only matched
        // bare field references; the MemberAccess unwrap extends it to qualified
        // field references, so the canonical `Interlocked.*` pattern no longer
        // silently drops as `Read`.
        const string source = """
            using System.Threading;
            namespace Sample;
            internal sealed class Accumulator
            {
                public int ErrorCount;
            }
            internal sealed class Consumer
            {
                public int Touch(Accumulator acc)
                {
                    Interlocked.Increment(ref acc.ErrorCount);
                    return acc.ErrorCount;
                }
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions { IncludePublic = true },
            default);

        var errorCount = hits.FirstOrDefault(hit => hit.SymbolName == "ErrorCount");
        Assert.IsNull(errorCount,
            "`Interlocked.Increment(ref acc.ErrorCount)` must count as a write on the qualified field reference.");
    }

    [TestMethod]
    public async Task FindDeadFields_LimitCapsResults()
    {
        const string source = """
            namespace Sample;
            internal sealed class Counter
            {
                private int _a;
                private int _b;
                private int _c;
            }
            """;

        var analyzer = BuildAnalyzerWithSource(source);

        var hits = await analyzer.FindDeadFieldsAsync(
            WorkspaceId,
            new DeadFieldsAnalysisOptions { Limit = 2 },
            default);

        Assert.AreEqual(2, hits.Count);
    }

    private UnusedCodeAnalyzer BuildAnalyzerWithSource(string source)
    {
        var workspace = UnusedCodeAnalyzerTestHarness.CreateWorkspace(source,
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ]);
        var harness = new UnusedCodeAnalyzerTestHarness(WorkspaceId, workspace);
        _harnesses.Add(harness);
        return harness.Analyzer;
    }
}
