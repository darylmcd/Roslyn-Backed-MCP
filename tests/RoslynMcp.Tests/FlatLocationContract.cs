using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

// locationdto-stage3-flat-field-deprecation (ADR 0001, stage 3): this class is the ONLY test code that reads the
// deprecated flat location members directly. It asserts the contract that must hold until the next major:
// the flat members stay populated and agree with the nested Location. Delete it with the flat members.
#pragma warning disable CS0618

/// <summary>Assertions that the deprecated flat location members agree with the nested <see cref="LocationDto"/>.</summary>
internal static class FlatLocationContract
{
    internal static void AssertAgreesWithLocation(SymbolDto dto)
    {
        Assert.IsNotNull(dto.Location);
        AssertSpan(dto.Location, dto.FilePath, dto.StartLine, dto.StartColumn, dto.EndLine, dto.EndColumn);
    }

    internal static void AssertAgreesWithLocation(DiagnosticDto dto)
    {
        Assert.IsNotNull(dto.Location);
        AssertSpan(dto.Location, dto.FilePath, dto.StartLine, dto.StartColumn, dto.EndLine, dto.EndColumn);
    }

    internal static void AssertAgreesWithLocation(TypeUsageDto dto)
    {
        Assert.IsNotNull(dto.Location);
        AssertSpan(dto.Location, dto.FilePath, dto.StartLine, dto.StartColumn, dto.EndLine, dto.EndColumn);
        Assert.AreEqual(dto.ContainingMember, dto.Location.ContainingMember);
        Assert.AreEqual(dto.PreviewText, dto.Location.PreviewText);
        Assert.AreEqual(dto.Classification.ToString(), dto.Location.Classification);
    }

    private static void AssertSpan(
        LocationDto location, string? filePath, int? startLine, int? startColumn, int? endLine, int? endColumn)
    {
        Assert.AreEqual(filePath, location.FilePath);
        Assert.AreEqual(startLine, location.StartLine);
        Assert.AreEqual(startColumn, location.StartColumn);
        Assert.AreEqual(endLine, location.EndLine);
        Assert.AreEqual(endColumn, location.EndColumn);
    }
}
