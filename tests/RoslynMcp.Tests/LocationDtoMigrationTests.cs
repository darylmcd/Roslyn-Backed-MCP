using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LocationDtoMigrationTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void AdditiveContracts_SerializeCamelCaseNestedLocation_AlongsideLegacyCoordinates()
    {
        var location = new LocationDto(
            FilePath: "sample.cs",
            StartLine: 2,
            StartColumn: 3,
            EndLine: 2,
            EndColumn: 8,
            ContainingMember: "Sample.M",
            PreviewText: "value",
            Classification: "Read");

        object[] contracts =
        [
            new SymbolDto(
                Name: "M",
                FullyQualifiedName: "Sample.M",
                SymbolHandle: null,
                Kind: "Method",
                ContainingType: "Sample",
                Namespace: null,
                Project: "Sample",
                FilePath: location.FilePath,
                StartLine: location.StartLine,
                StartColumn: location.StartColumn,
                EndLine: location.EndLine,
                EndColumn: location.EndColumn,
                ReturnType: "void",
                Parameters: null,
                Modifiers: null,
                BaseTypes: null,
                Interfaces: null,
                Documentation: null,
                Location: location),
            new DiagnosticDto(
                "CS1002", "; expected", "Error", "Compiler",
                location.FilePath, location.StartLine, location.StartColumn, location.EndLine, location.EndColumn,
                location),
            new TypeUsageDto(
                location.FilePath, location.StartLine, location.StartColumn, location.EndLine, location.EndColumn,
                location.ContainingMember, location.PreviewText, TypeUsageClassification.Other, location),
            new PropertyWriteDto(
                location.FilePath, location.StartLine, location.StartColumn, location.EndLine, location.EndColumn,
                location.ContainingMember, location.PreviewText, "Assignment", location),
            new MutationCallerDto(
                location.FilePath, location.StartLine, location.StartColumn,
                location.ContainingMember, location.PreviewText, "Runtime", location)
        ];

        foreach (var contract in contracts)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(contract, contract.GetType(), WireOptions));
            var root = document.RootElement;
            Assert.IsTrue(root.TryGetProperty("location", out var nested), contract.GetType().Name);
            Assert.IsFalse(root.TryGetProperty("Location", out _), contract.GetType().Name);
            Assert.AreEqual(location.FilePath, nested.GetProperty("filePath").GetString(), contract.GetType().Name);
            Assert.AreEqual(location.StartLine, nested.GetProperty("startLine").GetInt32(), contract.GetType().Name);
            Assert.AreEqual(location.StartLine, root.GetProperty("startLine").GetInt32(), contract.GetType().Name);
            Assert.AreEqual(location.StartColumn, root.GetProperty("startColumn").GetInt32(), contract.GetType().Name);
        }
    }

    [TestMethod]
    public void AdditiveContracts_DeserializeLegacyPayloads_WithNullNestedLocation()
    {
        var symbol = JsonSerializer.Deserialize<SymbolDto>(
            """{"name":"M","fullyQualifiedName":"Sample.M","kind":"Method"}""",
            WireOptions);
        var diagnostic = JsonSerializer.Deserialize<DiagnosticDto>(
            """{"id":"CS1002","message":"; expected","severity":"Error","category":"Compiler","filePath":"sample.cs","startLine":2,"startColumn":3,"endLine":2,"endColumn":8}""",
            WireOptions);
        var usage = JsonSerializer.Deserialize<TypeUsageDto>(
            """{"filePath":"sample.cs","startLine":2,"startColumn":3,"endLine":2,"endColumn":8,"classification":0}""",
            WireOptions);

        Assert.IsNull(symbol!.Location);
        Assert.IsNull(diagnostic!.Location);
        Assert.IsNull(usage!.Location);
    }

    [TestMethod]
    public void BuildDiagnosticParsing_OnlyCreatesCompleteNestedLocations()
    {
        var complete = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3,2,8): error CS1002: ; expected [Sample.csproj]").Single();
        var startOnly = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3): error CS1002: ; expected [Sample.csproj]").Single();

        Assert.IsNotNull(complete.Location);
        Assert.AreEqual(complete.FilePath, complete.Location.FilePath);
        Assert.AreEqual(complete.EndLine, complete.Location.EndLine);
        Assert.AreEqual(complete.EndColumn, complete.Location.EndColumn);
        Assert.IsNull(startOnly.Location, "A start-only MSBuild diagnostic must not fabricate an end span.");

        var enriched = SymbolMapper.WithEndPosition(startOnly, endLine: 2, endColumn: 8);
        Assert.IsNotNull(enriched.Location);
        Assert.AreEqual(enriched.FilePath, enriched.Location.FilePath);
        Assert.AreEqual(enriched.StartLine, enriched.Location.StartLine);
        Assert.AreEqual(enriched.StartColumn, enriched.Location.StartColumn);
        Assert.AreEqual(enriched.EndLine, enriched.Location.EndLine);
        Assert.AreEqual(enriched.EndColumn, enriched.Location.EndColumn);
    }
}
