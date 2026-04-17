using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <see cref="RoslynMcp.Roslyn.Services.ExceptionFlowService"/> — the
/// <c>trace_exception_flow</c> tool (initiative: <c>exception-handler-classification-tracer</c>).
/// The service walks every syntax tree's <c>CatchClauseSyntax</c> and returns each catch
/// whose declared type is assignable from the input exception type. Covers:
///   * a typed-exception catch (translated to JSON form, including the body excerpt + rethrow-as annotation)
///   * the untyped <c>catch { }</c> fallback (treated as catching <see cref="Exception"/>)
///   * an exact-match exception declared in the body
///   * the empty-result path for an unknown metadata name
///   * the <c>when</c>-filter case (filter source prepended to the body excerpt)
///   * the <c>maxResults</c> cap (truncated flag set)
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
