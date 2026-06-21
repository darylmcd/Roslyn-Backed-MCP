using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <see cref="RoslynMcp.Roslyn.Services.ExceptionFlowService"/> — the
/// <c>trace_exception_flow</c> tool (initiative: <c>exception-handler-classification-tracer</c>).
/// The service walks every syntax tree's <c>CatchClauseSyntax</c> and <c>throw new T(...)</c>
/// nodes in one pass, returning each catch whose declared type is assignable from the input and
/// each throw whose thrown type is assignable to it. Covers:
///   * a typed-exception catch (translated to JSON form, including the body excerpt + rethrow-as annotation)
///   * the untyped <c>catch { }</c> fallback (treated as catching <see cref="Exception"/>)
///   * an exact-match exception declared in the body
///   * the empty-result path for an unknown metadata name
///   * the <c>when</c>-filter case (filter source prepended to the body excerpt)
///   * the <c>maxResults</c> cap (truncated flag set + CountOmitted)
///   * throw-site collection with the syntactic unhandled-at-boundary classification
///   * type-specific catches ranked above base-Exception catches
/// </summary>
[TestClass]
public sealed class ExceptionFlowServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task TraceExceptionFlow_FindsTypedCatch_WithRethrowAsAndBodyExcerpt()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceCode = """
            using System;
            using System.Text.Json;

            namespace SampleLib.ErrorHandling;

            public static class Classifier
            {
                public static string ClassifyError(Exception ex, string source)
                {
                    try
                    {
                        if (ex is JsonException jsonEx)
                        {
                            return jsonEx.Message;
                        }
                        throw ex;
                    }
                    catch (JsonException jsonEx)
                    {
                        // Wraps the JsonException in a domain error.
                        throw new InvalidOperationException("bad JSON: " + jsonEx.Message, jsonEx);
                    }
                }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/Classifier.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.Text.Json.JsonException",
            scopeProjectFilter: "SampleLib",
            maxResults: null,
            CancellationToken.None);

        Assert.AreEqual("System.Text.Json.JsonException", result.ExceptionTypeMetadataName);
        Assert.IsNotNull(result.ResolvedTypeDisplayName, "Type should resolve against SampleLib references.");
        Assert.IsFalse(result.Truncated);

        var classifierSite = result.CatchSites.FirstOrDefault(s =>
            s.ContainingMethod is not null &&
            s.ContainingMethod.Contains("Classifier.ClassifyError", StringComparison.Ordinal));
        Assert.IsNotNull(classifierSite, "Expected the Classifier.ClassifyError catch site to be reported.");

        Assert.AreEqual("System.Text.Json.JsonException", classifierSite!.DeclaredExceptionTypeMetadataName);
        Assert.IsFalse(classifierSite.CatchesBaseException,
            "Exact type match should not set CatchesBaseException.");
        Assert.IsFalse(classifierSite.HasFilter);
        StringAssert.Contains(classifierSite.BodyExcerpt, "InvalidOperationException");
        Assert.AreEqual("System.InvalidOperationException", classifierSite.RethrowAsTypeMetadataName,
            "Catch body translates to InvalidOperationException, so rethrow-as should name it.");
    }

    [TestMethod]
    public async Task TraceExceptionFlow_MatchesBaseClassCatches_AndUntypedCatch()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceCode = """
            using System;

            namespace SampleLib.ErrorHandling;

            public static class BroadHandlers
            {
                public static void HandlesBaseException()
                {
                    try
                    {
                        DoWork();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                public static void HandlesUntyped()
                {
                    try
                    {
                        DoWork();
                    }
                    catch
                    {
                        // Untyped catch — CLR treats as System.Exception.
                    }
                }

                private static void DoWork() { }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/BroadHandlers.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.InvalidOperationException",
            scopeProjectFilter: "SampleLib",
            maxResults: null,
            CancellationToken.None);

        Assert.IsTrue(result.CatchSites.Count >= 2,
            "Both the typed Exception catch and the untyped catch should match InvalidOperationException.");

        Assert.IsTrue(result.CatchSites.All(s => s.CatchesBaseException),
            "Every site that catches System.Exception (or untyped) is wider than InvalidOperationException.");

        Assert.IsTrue(
            result.CatchSites.Any(s => s.ContainingMethod?.Contains("HandlesBaseException", StringComparison.Ordinal) == true),
            "HandlesBaseException's typed-Exception catch should be reported.");

        Assert.IsTrue(
            result.CatchSites.Any(s => s.ContainingMethod?.Contains("HandlesUntyped", StringComparison.Ordinal) == true),
            "Untyped catch should be reported as catching System.Exception.");
    }

    [TestMethod]
    public async Task TraceExceptionFlow_UnresolvedType_ReturnsEmptyList()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "Made.Up.Namespace.FakeException",
            scopeProjectFilter: null,
            maxResults: null,
            CancellationToken.None);

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(0, result.CatchSites.Count);
        Assert.IsNull(result.ResolvedTypeDisplayName,
            "No project resolves this metadata name, so the display name should be null.");
        Assert.IsFalse(result.Truncated);
    }

    [TestMethod]
    public async Task TraceExceptionFlow_WhenFilter_IncludesFilterSourceInBodyExcerpt()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceCode = """
            using System;

            namespace SampleLib.ErrorHandling;

            public static class FilteredHandlers
            {
                public static int FilteredCatch()
                {
                    try
                    {
                        return 0;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("retry"))
                    {
                        return 1;
                    }
                }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/FilteredHandlers.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.InvalidOperationException",
            scopeProjectFilter: "SampleLib",
            maxResults: null,
            CancellationToken.None);

        var filtered = result.CatchSites.FirstOrDefault(s =>
            s.ContainingMethod?.Contains("FilteredCatch", StringComparison.Ordinal) == true);
        Assert.IsNotNull(filtered);
        Assert.IsTrue(filtered!.HasFilter, "Catch has a `when` filter.");
        StringAssert.Contains(filtered.BodyExcerpt, "retry",
            "Filter source must be prepended to the body excerpt so agents see the filter condition.");
    }

    [TestMethod]
    public async Task TraceExceptionFlow_ReturnsThrowSites_ForTracedType()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceCode = """
            using System;

            namespace SampleLib.ErrorHandling;

            public static class Originator
            {
                public static void Validate(int value)
                {
                    if (value < 0)
                    {
                        // Throw OUTSIDE any catch — should be reported as unhandled at boundary.
                        throw new InvalidOperationException("value must be non-negative");
                    }
                }

                public static void Wrap()
                {
                    try
                    {
                        Validate(-1);
                    }
                    catch (Exception)
                    {
                        // Throw INSIDE a catch — should be reported as handled (not at boundary).
                        throw new InvalidOperationException("wrapped");
                    }
                }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/Originator.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.InvalidOperationException",
            scopeProjectFilter: "SampleLib",
            maxResults: null,
            CancellationToken.None);

        Assert.IsTrue(result.ThrowSites.Count >= 1,
            "A `throw new InvalidOperationException(...)` of the traced type must be reported as a throw site.");
        Assert.AreEqual(result.ThrowSites.Count, result.ThrowSiteCount,
            "ThrowSiteCount must mirror ThrowSites.Count.");
        Assert.AreEqual(0, result.CountOmitted, "Nothing was truncated, so CountOmitted must be 0.");

        var boundaryThrow = result.ThrowSites.FirstOrDefault(s =>
            s.ContainingMethod?.Contains("Originator.Validate", StringComparison.Ordinal) == true);
        Assert.IsNotNull(boundaryThrow, "The Validate throw site should be reported.");
        Assert.IsTrue(boundaryThrow!.IsUnhandledAtBoundary,
            "A throw not lexically inside a catch must be flagged unhandled at boundary.");
        StringAssert.Contains(boundaryThrow.ExpressionExcerpt, "InvalidOperationException");

        var wrappedThrow = result.ThrowSites.FirstOrDefault(s =>
            s.ContainingMethod?.Contains("Originator.Wrap", StringComparison.Ordinal) == true);
        Assert.IsNotNull(wrappedThrow, "The throw inside Wrap's catch should be reported.");
        Assert.IsFalse(wrappedThrow!.IsUnhandledAtBoundary,
            "A throw lexically inside a catch must NOT be flagged unhandled at boundary.");
    }

    [TestMethod]
    public async Task TraceExceptionFlow_TypeSpecificCatchRanksAboveBaseException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        // The base-Exception catch is lexically FIRST so a stable, unranked walk would emit it
        // before the type-specific catch. Ranking must reorder the exact-match catch ahead of it.
        var sourceCode = """
            using System;

            namespace SampleLib.ErrorHandling;

            public static class RankingSample
            {
                public static void BroadFirst()
                {
                    try { } catch (Exception) { }
                }

                public static void SpecificSecond()
                {
                    try { } catch (InvalidOperationException) { }
                }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/RankingSample.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.InvalidOperationException",
            scopeProjectFilter: "SampleLib",
            maxResults: null,
            CancellationToken.None);

        var specificIndex = IndexOfSite(result.CatchSites, "SpecificSecond");
        var broadIndex = IndexOfSite(result.CatchSites, "BroadFirst");

        Assert.IsTrue(specificIndex >= 0, "The InvalidOperationException catch must be reported.");
        Assert.IsTrue(broadIndex >= 0, "The base-Exception catch must be reported (it is wider).");
        Assert.IsTrue(specificIndex < broadIndex,
            "The type-specific (exact-match) catch must rank ahead of the broad base-Exception catch, "
            + "even though the broad catch appears first in source order.");

        var specific = result.CatchSites[specificIndex];
        Assert.IsFalse(specific.CatchesBaseException, "Exact type match must not set CatchesBaseException.");
        Assert.IsTrue(result.CatchSites[broadIndex].CatchesBaseException,
            "The base-Exception catch is wider than InvalidOperationException.");
    }

    private static int IndexOfSite(IReadOnlyList<ExceptionCatchSiteDto> sites, string methodFragment)
    {
        for (var i = 0; i < sites.Count; i++)
        {
            if (sites[i].ContainingMethod?.Contains(methodFragment, StringComparison.Ordinal) == true)
            {
                return i;
            }
        }
        return -1;
    }

    [TestMethod]
    public async Task TraceExceptionFlow_MaxResults_TruncatesAndSetsFlag()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var sourceCode = """
            using System;

            namespace SampleLib.ErrorHandling;

            public static class ManyHandlers
            {
                public static void A() { try { } catch (Exception) { } }
                public static void B() { try { } catch (Exception) { } }
                public static void C() { try { } catch (Exception) { } }
                public static void D() { try { } catch (Exception) { } }
                public static void E() { try { } catch (Exception) { } }
            }
            """;
        await WriteFileAsync(workspace, "SampleLib/ManyHandlers.cs", sourceCode);

        var result = await ExceptionFlowService.TraceExceptionFlowAsync(
            workspace.WorkspaceId,
            "System.Exception",
            scopeProjectFilter: "SampleLib",
            maxResults: 2,
            CancellationToken.None);

        Assert.AreEqual(2, result.CatchSites.Count);
        Assert.IsTrue(result.Truncated, "When the cap is hit the Truncated flag must be set.");
        Assert.AreEqual(3, result.CountOmitted,
            "5 catch sites discovered, cap of 2 retained, so 3 must be reported omitted.");
    }

    private static async Task WriteFileAsync(
        IsolatedWorkspaceScope workspace,
        string relativePath,
        string contents)
    {
        var fullPath = workspace.GetPath(relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullPath, contents, CancellationToken.None);
        await workspace.ReloadAsync(CancellationToken.None);
    }
}
