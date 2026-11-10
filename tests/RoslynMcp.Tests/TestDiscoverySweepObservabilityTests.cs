using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// test-discovery-direct-reference-failure-observability: a failed semantic reference sweep must be
/// logged, must not throw (fallback results survive), must be visible in the public diagnostics
/// (one payload-safe MissReasons entry per call, heuristic not listed as attempted) and must never
/// swallow cancellation.
/// </summary>
[TestClass]
public sealed class TestDiscoverySweepObservabilityTests
{
    private const string SecretText = "boom-secret-exception-text";

    // Matches by name affinity (display name contains "AlphaProbe") without referencing the type.
    private const string NameMatchedTestSource =
        "namespace Verification; class First { [TestMethod] void AlphaProbe_Works() { } }";

    // Matches only through the semantic reference sweep.
    private const string ReferencingTestSource =
        "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); } }";

    private static Task<IEnumerable<ReferencedSymbol>> Throwing(ISymbol symbol, Solution solution, CancellationToken ct)
        => throw new InvalidOperationException(SecretText);

    private static Task<IEnumerable<ReferencedSymbol>> Cancelling(ISymbol symbol, Solution solution, CancellationToken ct)
        => throw new OperationCanceledException();

    [TestMethod]
    public async Task FileRoute_DirectSweepFailure_KeepsFallbackAndReportsIncompleteOnce()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", NameMatchedTestSource);
        var logger = new CapturingLogger();
        var service = fixture.CreateService(logger);
        service.FindReferencesAsync = Throwing;

        var result = await service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count, "name-affinity fallback result must survive the failed sweep");
        var incomplete = result.Diagnostics.MissReasons.Where(r => r.Contains("did not complete", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(1, incomplete.Count, "exactly one incompleteness entry per call");
        Assert.IsTrue(incomplete[0].Contains("1 input file(s)", StringComparison.Ordinal));
        AssertPayloadSafe(result.Diagnostics, fixture.Source);
        Assert.IsFalse(result.Diagnostics.HeuristicsAttempted.Contains("direct-reference"));
        Assert.IsTrue(result.Diagnostics.HeuristicsAttempted.Contains("type-name"));
        Assert.IsTrue(logger.Exceptions.Any(e => e.Message == SecretText), "exception must reach the logger");
    }

    [TestMethod]
    public async Task FileRoute_DirectAndFallbackSweepFailure_CountsFileOnceAndOmitsFailedHeuristics()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: true);
        fixture.AddTestFile("Tests.A", ReferencingTestSource);
        var logger = new CapturingLogger();
        var service = fixture.CreateService(logger);
        service.FindReferencesAsync = Throwing;

        var result = await service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);

        Assert.AreEqual(0, result.Tests.Count);
        var incomplete = result.Diagnostics.MissReasons.Where(r => r.Contains("did not complete", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(1, incomplete.Count);
        Assert.IsTrue(incomplete[0].Contains("1 input file(s)", StringComparison.Ordinal), "direct + inbound failure on one file is one file");
        AssertPayloadSafe(result.Diagnostics, fixture.Source);
        Assert.IsFalse(result.Diagnostics.HeuristicsAttempted.Contains("direct-reference"));
        Assert.IsFalse(result.Diagnostics.HeuristicsAttempted.Contains("inbound-reference"));
        Assert.IsTrue(result.Diagnostics.HeuristicsAttempted.Contains("namespace-neighbor"), "the neighbor expansion completed");
        Assert.IsTrue(logger.Exceptions.Count(e => e.Message == SecretText) >= 2, "both sweep failures are logged");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileRoute_Cancellation_Propagates(bool forceInboundFallback)
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback);
        fixture.AddTestFile("Tests.A", ReferencingTestSource);
        var logger = new CapturingLogger();
        var service = fixture.CreateService(logger);
        service.FindReferencesAsync = Cancelling;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None));
        Assert.AreEqual(0, logger.Exceptions.Count, "cancellation is not a discovery failure");
    }

    [TestMethod]
    public async Task FileRoute_SuccessfulSweep_HasNoIncompleteReasonAndKeepsHeuristics()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", ReferencingTestSource);
        var logger = new CapturingLogger();

        var result = await fixture.CreateService(logger).FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count);
        Assert.IsFalse(result.Diagnostics.MissReasons.Any(r => r.Contains("did not complete", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            new[] { "direct-reference", "type-name", "file-name" },
            result.Diagnostics.HeuristicsAttempted.ToArray());
        Assert.AreEqual(0, logger.Exceptions.Count);
    }

    [TestMethod]
    public async Task SymbolRoute_SweepFailure_KeepsHeuristicResultsAndReportsIncomplete()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", NameMatchedTestSource);
        var logger = new CapturingLogger();
        var service = fixture.CreateService(logger);
        service.FindReferencesAsync = Throwing;

        var result = await service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count);
        var incomplete = result.Diagnostics.MissReasons.Where(r => r.Contains("did not complete", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(1, incomplete.Count);
        AssertPayloadSafe(result.Diagnostics, fixture.Source);
        Assert.IsFalse(result.Diagnostics.HeuristicsAttempted.Contains("reference-sweep"));
        Assert.IsTrue(result.Diagnostics.HeuristicsAttempted.Contains("symbol-name"));
        Assert.IsTrue(logger.Exceptions.Any(e => e.Message == SecretText));
    }

    [TestMethod]
    public async Task SymbolRoute_Cancellation_Propagates()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", ReferencingTestSource);
        var service = fixture.CreateService();
        service.FindReferencesAsync = Cancelling;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None));
    }

    [TestMethod]
    public async Task SymbolRoute_SuccessfulSweep_HasNoIncompleteReasonAndKeepsHeuristics()
    {
        using var fixture = new TestDiscoveryProjectIdentityTests.MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", ReferencingTestSource);

        var result = await fixture.CreateService().FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count);
        Assert.IsFalse(result.Diagnostics.MissReasons.Any(r => r.Contains("did not complete", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            new[] { "symbol-name", "container-name", "reference-sweep" },
            result.Diagnostics.HeuristicsAttempted.ToArray());
    }

    private static void AssertPayloadSafe(RelatedTestsDiagnosticsDto diagnostics, string sourcePath)
    {
        foreach (var reason in diagnostics.MissReasons)
        {
            Assert.IsFalse(reason.Contains(SecretText, StringComparison.Ordinal), "exception text leaked into diagnostics");
            // Pre-existing per-file zero-match reasons legitimately quote the input path; the new entry must not.
            if (reason.Contains("did not complete", StringComparison.Ordinal))
            {
                Assert.IsFalse(reason.Contains(sourcePath, StringComparison.OrdinalIgnoreCase), "caller path leaked into the incompleteness entry");
                Assert.IsFalse(reason.Contains("Library.cs", StringComparison.OrdinalIgnoreCase));
            }
            Assert.IsFalse(reason.Contains("InvalidOperationException", StringComparison.Ordinal));
        }
    }

    private sealed class CapturingLogger : ILogger<TestDiscoveryService>
    {
        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
