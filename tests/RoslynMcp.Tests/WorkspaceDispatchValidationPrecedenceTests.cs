using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceDispatchValidationPrecedenceTests
{
    [TestMethod]
    [DataRow("list-analyzers", false)]
    [DataRow("find-consumers", false)]
    [DataRow("callers-callees", false)]
    [DataRow("impact-references", false)]
    [DataRow("impact-declarations", false)]
    [DataRow("find-type-mutations", false)]
    [DataRow("find-type-usages", false)]
    [DataRow("semantic-grep", false)]
    [DataRow("symbol-search-pagination", false)]
    [DataRow("symbol-search-cap", false)]
    [DataRow("find-references", false)]
    [DataRow("document-symbols-required", false)]
    [DataRow("symbol-relationships", false)]
    [DataRow("bulk-cap", false)]
    [DataRow("bulk-required", false)]
    [DataRow("bulk-size", false)]
    [DataRow("valid-pagination", true)]
    [DataRow("valid-symbol-cap", true)]
    [DataRow("valid-impact-limit", true)]
    [DataRow("valid-required-field", true)]
    [DataRow("valid-bulk", true)]
    public async Task RequestValidation_PrecedesUnknownWorkspaceDispatch(string scenario, bool valid)
    {
        var gate = new DispatchRejectingGate();

        try
        {
            await InvokeScenarioAsync(scenario, gate);
            Assert.Fail("The test gate or parameter validation should stop every scenario.");
        }
        catch (DispatchReachedException) when (valid)
        {
            Assert.AreEqual(1, gate.ReadCallCount,
                "A valid request must reach workspace dispatch exactly once.");
        }
        catch (ArgumentException) when (!valid)
        {
            Assert.AreEqual(0, gate.ReadCallCount,
                "An invalid request must fail before the unknown workspace is dispatched.");
        }
    }

    private static Task InvokeScenarioAsync(string scenario, IWorkspaceExecutionGate gate) => scenario switch
    {
        "list-analyzers" => AnalyzerInfoTools.ListAnalyzers(gate, null!, "missing-workspace", offset: -1),
        "find-consumers" => ConsumerAnalysisTools.FindConsumers(gate, null!, "missing-workspace", offset: -1),
        "callers-callees" => AnalysisTools.GetCallersCallees(gate, null!, "missing-workspace", callersLimit: 0),
        "impact-references" => AnalysisTools.AnalyzeImpact(gate, null!, "missing-workspace", referencesOffset: -1),
        "impact-declarations" => AnalysisTools.AnalyzeImpact(gate, null!, "missing-workspace", declarationsLimit: 0),
        "find-type-mutations" => AnalysisTools.FindTypeMutations(gate, null!, "missing-workspace", limit: 0),
        "find-type-usages" => AnalysisTools.FindTypeUsages(gate, null!, "missing-workspace", offset: -1),
        "semantic-grep" => AnalysisTools.SemanticGrep(gate, null!, "missing-workspace", "x", offset: -1),
        "symbol-search-pagination" => SymbolTools.SearchSymbols(null, gate, null!, "missing-workspace", "x", offset: -1),
        "symbol-search-cap" => SymbolTools.SearchSymbols(null, gate, null!, "missing-workspace", "x", limit: 51),
        "find-references" => SymbolTools.FindReferences(null, null!, gate, null!, "missing-workspace", limit: 0),
        "document-symbols-required" => SymbolTools.GetDocumentSymbols(
            null!, gate, null!, "missing-workspace", filePath: null, symbolHandle: null, metadataName: null),
        "symbol-relationships" => SymbolTools.GetSymbolRelationships(gate, null!, "missing-workspace", limit: 0),
        "bulk-cap" => SymbolTools.FindReferencesBulk(gate, null!, "missing-workspace", [], maxItemsPerSymbol: 0),
        "bulk-required" => SymbolTools.FindReferencesBulk(gate, null!, "missing-workspace", null!),
        "bulk-size" => SymbolTools.FindReferencesBulk(
            gate,
            null!,
            "missing-workspace",
            Enumerable.Range(0, 51).Select(_ => new BulkSymbolLocator(
                SymbolHandle: null,
                MetadataName: "Example.Type",
                FilePath: null,
                Line: null,
                Column: null)).ToArray()),
        "valid-pagination" => AnalyzerInfoTools.ListAnalyzers(gate, null!, "missing-workspace", offset: 0, limit: 1),
        "valid-symbol-cap" => SymbolTools.SearchSymbols(null, gate, null!, "missing-workspace", "x", limit: 50),
        "valid-impact-limit" => AnalysisTools.AnalyzeImpact(gate, null!, "missing-workspace", declarationsLimit: 1),
        "valid-required-field" => SymbolTools.GetDocumentSymbols(
            null!, gate, null!, "missing-workspace", filePath: "C:/repo/File.cs"),
        "valid-bulk" => SymbolTools.FindReferencesBulk(gate, null!, "missing-workspace", [], maxItemsPerSymbol: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown validation scenario."),
    };

    private sealed class DispatchRejectingGate : IWorkspaceExecutionGate
    {
        public int ReadCallCount { get; private set; }

        public Task<T> RunReadAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct)
        {
            ReadCallCount++;
            throw new DispatchReachedException();
        }

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            throw new NotSupportedException();

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            throw new NotSupportedException();

        public void RemoveGate(string workspaceId) => throw new NotSupportedException();
    }

    private sealed class DispatchReachedException : Exception;
}
