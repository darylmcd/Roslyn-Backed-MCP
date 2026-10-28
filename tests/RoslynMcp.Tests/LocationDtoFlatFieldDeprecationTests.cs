using System.Reflection;
using System.Text.Json;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Stage 3 of ADR 0001: the legacy flat location members are deprecated (warning, never error) in
/// favour of the nested <see cref="LocationDto"/>, while staying serialized and populated until the
/// next major.
/// </summary>
[TestClass]
public sealed class LocationDtoFlatFieldDeprecationTests
{
    private static readonly string[] CoordinateFields = ["FilePath", "StartLine", "StartColumn", "EndLine", "EndColumn"];

    private static readonly string[] TypeUsageFields =
        [.. CoordinateFields, "ContainingMember", "PreviewText", "Classification"];

    public static IEnumerable<object[]> LegacyFlatMembers()
    {
        foreach (var field in CoordinateFields)
        {
            yield return [typeof(SymbolDto), field];
            yield return [typeof(DiagnosticDto), field];
        }

        foreach (var field in TypeUsageFields)
        {
            yield return [typeof(TypeUsageDto), field];
        }
    }

    [TestMethod]
    [DynamicData(nameof(LegacyFlatMembers))]
    public void LegacyFlatMember_IsObsoleteWarning_PointingAtLocation(Type dtoType, string memberName)
    {
        var property = dtoType.GetProperty(memberName);

        Assert.IsNotNull(property, $"{dtoType.Name}.{memberName} must remain on the contract until the next major.");
        var obsolete = property.GetCustomAttribute<ObsoleteAttribute>();
        Assert.IsNotNull(obsolete, $"{dtoType.Name}.{memberName} must be [Obsolete].");
        Assert.IsFalse(obsolete.IsError, $"{dtoType.Name}.{memberName} must warn, not error, in a minor release.");
        StringAssert.Contains(obsolete.Message, "Location", $"{dtoType.Name}.{memberName} message must name the replacement.");
    }

    [TestMethod]
    [DataRow(typeof(SymbolDto))]
    [DataRow(typeof(DiagnosticDto))]
    [DataRow(typeof(TypeUsageDto))]
    public void NestedLocation_RemainsTheSupportedReplacement(Type dtoType)
    {
        var property = dtoType.GetProperty("Location");

        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(LocationDto), property.PropertyType);
        Assert.IsNull(property.GetCustomAttribute<ObsoleteAttribute>());
    }

    [TestMethod]
    public void DeprecatedFlatMembers_StillSerializeAlongsideLocation()
    {
        var location = new LocationDto("sample.cs", 2, 3, 2, 8, "Sample.M", "value", "Read");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new TypeUsageDto(
                "sample.cs", 2, 3, 2, 8, "Sample.M", "value", TypeUsageClassification.Other, location),
            options));
        var root = document.RootElement;

        foreach (var name in new[]
                 {
                     "filePath", "startLine", "startColumn", "endLine", "endColumn",
                     "containingMember", "previewText", "classification", "location",
                 })
        {
            Assert.IsTrue(root.TryGetProperty(name, out _), $"'{name}' must stay on the wire during the deprecation window.");
        }
    }
}
