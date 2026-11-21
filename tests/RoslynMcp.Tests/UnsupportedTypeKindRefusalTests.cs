using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class UnsupportedTypeKindRefusalTests
{
    [TestMethod]
    [DataRow(SyntaxKind.EnumDeclaration, "Enum")]
    [DataRow(SyntaxKind.DelegateDeclaration, "Delegate")]
    [DataRow(SyntaxKind.NamespaceDeclaration, "NamespaceDeclaration")]
    public void Create_Names_The_Kind_And_The_Calling_Tool(SyntaxKind kind, string expectedName)
    {
        var refusal = UnsupportedTypeKindRefusal.Create(kind, "some_tool_preview");

        Assert.AreEqual(
            $"type-kind {expectedName} not supported for this refactor. " +
            "some_tool_preview currently supports class, struct, record, and interface declarations only.",
            refusal.Message);
    }
}
