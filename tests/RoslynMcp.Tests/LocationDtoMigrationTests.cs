using System.Reflection;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// ADR 0001 stage 3 completed at 5.0: <see cref="SymbolDto"/>, <see cref="DiagnosticDto"/> and
/// <see cref="TypeUsageDto"/> expose their position only through the nested <see cref="LocationDto"/>,
/// whose end coordinates are nullable so a start-only span survives.
/// </summary>
[TestClass]
public sealed class LocationDtoMigrationTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] RemovedFlatFields =
        ["filePath", "startLine", "startColumn", "endLine", "endColumn", "containingMember", "previewText", "classification"];

    [TestMethod]
    [DataRow(typeof(SymbolDto))]
    [DataRow(typeof(DiagnosticDto))]
    [DataRow(typeof(TypeUsageDto))]
    public void PrimaryContracts_HaveNoFlatLocationMembers(Type dtoType)
    {
        var flat = dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => RemovedFlatFields.Contains(JsonNamingPolicy.CamelCase.ConvertName(name)))
            .Where(name => !(dtoType == typeof(DiagnosticDto) && name == nameof(DiagnosticDto.FilePath)))
            .ToList();

        Assert.AreEqual(0, flat.Count, $"{dtoType.Name} must not expose flat location members: {string.Join(", ", flat)}");
        Assert.IsNotNull(dtoType.GetProperty("Location"));
    }

    [TestMethod]
    public void DiagnosticFilePath_IsASupportedNonObsoleteMember()
    {
        var property = typeof(DiagnosticDto).GetProperty(nameof(DiagnosticDto.FilePath));

        Assert.IsNotNull(property);
        Assert.IsNull(property.GetCustomAttribute<ObsoleteAttribute>(), "4.4.0 marked it obsolete; 5.0 reverses that for diagnostics.");
    }

    [TestMethod]
    public void DiagnosticFilePath_SerializesAsFilePath_AndAgreesWithLocation()
    {
        var location = new LocationDto("sample.cs", 2, 3, 2, 8, null, null);
        var positioned = new DiagnosticDto("CS1002", "; expected", "Error", "Compiler", location.FilePath, location);
        var fileOnly = new DiagnosticDto("NU1201", "incompatible", "Error", "Build", "App.csproj");

        using var a = JsonDocument.Parse(JsonSerializer.Serialize(positioned, WireOptions));
        using var b = JsonDocument.Parse(JsonSerializer.Serialize(fileOnly, WireOptions));

        Assert.AreEqual("sample.cs", a.RootElement.GetProperty("filePath").GetString());
        Assert.AreEqual(a.RootElement.GetProperty("filePath").GetString(), a.RootElement.GetProperty("location").GetProperty("filePath").GetString());
        Assert.AreEqual("App.csproj", b.RootElement.GetProperty("filePath").GetString());
        Assert.AreEqual(JsonValueKind.Null, b.RootElement.GetProperty("location").ValueKind);
    }

    [TestMethod]
    public void PrimaryContracts_SerializeOnlyANestedCamelCaseLocation()
    {
        var location = new LocationDto("sample.cs", 2, 3, 2, 8, "Sample.M", "value", "Read");

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
                ReturnType: "void",
                Parameters: null,
                Modifiers: null,
                BaseTypes: null,
                Interfaces: null,
                Documentation: null,
                Location: location),
            new DiagnosticDto("CS1002", "; expected", "Error", "Compiler", FilePath: location.FilePath, Location: location),
            new TypeUsageDto(location),
        ];

        foreach (var contract in contracts)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(contract, contract.GetType(), WireOptions));
            var root = document.RootElement;
            var name = contract.GetType().Name;
            Assert.IsTrue(root.TryGetProperty("location", out var nested), name);
            Assert.IsFalse(root.TryGetProperty("Location", out _), name);
            Assert.AreEqual(location.FilePath, nested.GetProperty("filePath").GetString(), name);
            Assert.AreEqual(location.StartLine, nested.GetProperty("startLine").GetInt32(), name);
            Assert.AreEqual(location.EndColumn, nested.GetProperty("endColumn").GetInt32(), name);
            foreach (var removed in RemovedFlatFields.Where(r => !(contract is DiagnosticDto && r == "filePath")))
            {
                Assert.IsFalse(root.TryGetProperty(removed, out _), $"{name} must not serialize flat '{removed}'.");
            }
        }
    }

    [TestMethod]
    public void SecondaryContracts_KeepTheirFlatCoordinatesAlongsideLocation()
    {
        // locationdto-secondary-dto-flat-field-lifecycle (open decision): PropertyWriteDto and
        // MutationCallerDto are outside ADR 0001 stage 3 and keep flat-plus-nested for now.
        var location = new LocationDto("sample.cs", 2, 3, 2, 8, "Sample.M", "value", "Read");
        object[] contracts =
        [
            new PropertyWriteDto(
                "sample.cs", 2, 3, 2, 8, "Sample.M", "value", "Assignment", location),
            new MutationCallerDto("sample.cs", 2, 3, "Sample.M", "value", "Runtime", location),
        ];

        foreach (var contract in contracts)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(contract, contract.GetType(), WireOptions));
            Assert.IsTrue(document.RootElement.TryGetProperty("location", out _), contract.GetType().Name);
            Assert.AreEqual(2, document.RootElement.GetProperty("startLine").GetInt32(), contract.GetType().Name);
        }
    }

    [TestMethod]
    public void BuildDiagnosticParsing_CompleteSpanKeepsItsEnd_StartOnlySpanKeepsItsStart()
    {
        var complete = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3,2,8): error CS1002: ; expected [Sample.csproj]").Single();
        var startOnly = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3): error CS1002: ; expected [Sample.csproj]").Single();

        Assert.AreEqual(new LocationDto("sample.cs", 2, 3, 2, 8, null, null), complete.Location);

        Assert.IsNotNull(startOnly.Location, "A start-only MSBuild diagnostic must keep its position.");
        Assert.AreEqual("sample.cs", startOnly.Location.FilePath);
        Assert.AreEqual(2, startOnly.Location.StartLine);
        Assert.AreEqual(3, startOnly.Location.StartColumn);
        Assert.IsNull(startOnly.Location.EndLine, "The end must not be fabricated.");
        Assert.IsNull(startOnly.Location.EndColumn, "The end must not be fabricated.");
    }

    [TestMethod]
    public void StartOnlyDiagnostic_SerializesWithoutAnEnd_AndSurvivesEnrichment()
    {
        var startOnly = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3): error CS1002: ; expected [Sample.csproj]").Single();

        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(startOnly, WireOptions)))
        {
            var location = document.RootElement.GetProperty("location");
            Assert.AreEqual(2, location.GetProperty("startLine").GetInt32());
            Assert.AreEqual(3, location.GetProperty("startColumn").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, location.GetProperty("endLine").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, location.GetProperty("endColumn").ValueKind);
        }

        var enriched = SymbolMapper.WithEndPosition(startOnly, endLine: 2, endColumn: 8);
        Assert.AreEqual(new LocationDto("sample.cs", 2, 3, 2, 8, null, null), enriched.Location);
    }

    [TestMethod]
    public void ToOptionalLocationDto_RequiresPathAndStart_AndKeepsAnEndOnlyWhenBothCoordinatesExist()
    {
        Assert.IsNull(SymbolMapper.ToOptionalLocationDto(null, 1, 1, 1, 2));
        Assert.IsNull(SymbolMapper.ToOptionalLocationDto("a.cs", null, 1, 1, 2));
        Assert.IsNull(SymbolMapper.ToOptionalLocationDto("a.cs", 1, null, 1, 2));

        var half = SymbolMapper.ToOptionalLocationDto("a.cs", 1, 2, 3, null);
        Assert.IsNotNull(half);
        Assert.IsNull(half.EndLine, "A lone end coordinate must not produce a half-populated end.");
        Assert.IsNull(half.EndColumn);
    }

    [TestMethod]
    public void WithEndPosition_LeavesALocationlessDiagnosticUnchanged()
    {
        var diagnostic = new DiagnosticDto("NU1101", "unable to find package", "Error", "Build", null);

        Assert.AreSame(diagnostic, SymbolMapper.WithEndPosition(diagnostic, 2, 8));
    }

    [TestMethod]
    public void Deserialize_PrimaryContractWithoutLocation_YieldsNullLocation()
    {
        var symbol = JsonSerializer.Deserialize<SymbolDto>(
            """{"name":"M","fullyQualifiedName":"Sample.M","kind":"Method"}""",
            WireOptions);

        Assert.IsNull(symbol!.Location);
    }
}
