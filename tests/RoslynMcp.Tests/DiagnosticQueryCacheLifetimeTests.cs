using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// test-discovery-cache-session-lifetime (same-mechanism instance): diagnostics are cached after the
/// analysis awaits; a close that completes during the analysis must not be followed by a republish.
/// A whole-solution query publishes both the raw-diagnostic cache and the result cache; a
/// project-filtered query publishes only the result cache, so both publish sites are exercised.
/// </summary>
[TestClass]
public sealed class DiagnosticQueryCacheLifetimeTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("SampleLib")]
    public Task CloseDuringAnalysis_DoesNotRepublishDiagnostics(string? projectFilter) =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            using var compilationCache = new CompilationCache(manager);
            var closing = new ClosingCompilationCache(compilationCache);
            var service = new DiagnosticService(
                manager,
                closing,
                new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance));
            closing.AfterAnalyzerCompilation = workspaceId => closing.Closed |= manager.Close(workspaceId);

            await service.GetDiagnosticsAsync(id, projectFilter, null, null, null, CancellationToken.None);

            Assert.IsTrue(closing.Closed, "The close must land while the analysis is in flight.");
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    [TestMethod]
    public Task Control_LiveWorkspaceCachesAndCloseEvicts() =>
        RealWorkspaceLifecycleFixture.RunAsync(1, async (manager, paths) =>
        {
            var id = (await manager.LoadAsync(paths[0], EvictPolicy.Strict, CancellationToken.None)).WorkspaceId;
            using var compilationCache = new CompilationCache(manager);
            var service = new DiagnosticService(
                manager,
                compilationCache,
                new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance));

            await service.GetDiagnosticsAsync(id, null, null, null, null, CancellationToken.None);
            Assert.AreEqual(1, service.CachedWorkspaceCount);

            Assert.IsTrue(manager.Close(id));
            Assert.AreEqual(0, service.CachedWorkspaceCount);
        });

    private sealed class ClosingCompilationCache(ICompilationCache inner) : ICompilationCache
    {
        public Action<string>? AfterAnalyzerCompilation { get; set; }
        public bool Closed { get; set; }

        public Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct) =>
            inner.GetCompilationAsync(workspaceId, project, ct);

        public Task<CompilationSnapshot?> GetCompilationSnapshotAsync(
            string workspaceId,
            Project project,
            CancellationToken ct) => inner.GetCompilationSnapshotAsync(workspaceId, project, ct);

        public async Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(
            string workspaceId,
            Project project,
            CancellationToken ct)
        {
            var result = await inner.GetCompilationWithAnalyzersAsync(workspaceId, project, ct).ConfigureAwait(false);
            AfterAnalyzerCompilation?.Invoke(workspaceId);
            return result;
        }

        public bool IsLiveSolution(string workspaceId, Solution solution) => inner.IsLiveSolution(workspaceId, solution);

        public void Invalidate(string workspaceId) => inner.Invalidate(workspaceId);
    }
}
