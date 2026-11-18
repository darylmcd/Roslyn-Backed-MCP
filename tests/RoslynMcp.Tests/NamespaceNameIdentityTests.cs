using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NamespaceNameIdentityTests
{
    private static CompilationUnitSyntax Parse(string source) =>
        (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source).GetRoot();

    [TestMethod]
    [DataRow("Old.Sub", "Old.Sub")]
    [DataRow("@Old.Sub", "Old.Sub")]
    [DataRow("Old . Sub ", "Old.Sub")]
    [DataRow("Target.@class", "Target.class")]
    [DataRow("Target.class", "Target.class")]
    [DataRow("@a.@b", "a.b")]
    [DataRow("", "")]
    [DataRow("  ", "")]
    public void Of_String_Drops_Escapes_And_Whitespace(string spelling, string expected) =>
        Assert.AreEqual(expected, NamespaceNameIdentity.Of(spelling));

    [TestMethod]
    [DataRow("using @Old.Sub;")]
    [DataRow("using Old . Sub ;")]
    [DataRow("using Old.Sub;")]
    [DataRow("global using Old.Sub;")]
    public void Of_Name_Is_The_Same_For_Every_Spelling_Of_A_Namespace(string import)
    {
        var directive = Parse(import).Usings.Single();

        Assert.AreEqual("Old.Sub", NamespaceNameIdentity.Of(directive.Name));
        Assert.IsTrue(NamespaceNameIdentity.ImportsNamespace(directive, "Old.Sub"));
        Assert.IsFalse(NamespaceNameIdentity.ImportsNamespace(directive, "Old"));
    }

    [TestMethod]
    [DataRow("using @Old.Sub;", "@Old.Sub")]
    [DataRow("using Old . Sub ;", "Old.Sub")]
    [DataRow("using Old.Sub;", "Old.Sub")]
    public void SourceSpelling_Keeps_Escapes_And_Drops_All_Trivia(string import, string expected) =>
        Assert.AreEqual(expected, NamespaceNameIdentity.SourceSpelling(Parse(import).Usings.Single().Name));

    [TestMethod]
    [DataRow("using A = Old.Sub;")]
    [DataRow("using static Old.Sub.Type;")]
    public void ImportsNamespace_Ignores_Alias_And_Static_Imports(string import)
    {
        var directive = Parse(import).Usings.Single();

        Assert.IsFalse(NamespaceNameIdentity.IsPlainImport(directive));
        Assert.IsFalse(NamespaceNameIdentity.ImportsNamespace(directive, NamespaceNameIdentity.Of(directive.Name)));
    }

    [TestMethod]
    public void Of_Symbol_Uses_Unescaped_Names_And_Global_Is_Empty()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Source.@class { public class W { } }");
        var compilation = CSharpCompilation.Create("identity", [tree]);
        var symbol = compilation.GetTypeByMetadataName("Source.class.W")!.ContainingNamespace;

        Assert.AreEqual("Source.class", NamespaceNameIdentity.Of(symbol));
        Assert.AreEqual(string.Empty, NamespaceNameIdentity.Of(compilation.GlobalNamespace));
        Assert.AreEqual(string.Empty, NamespaceNameIdentity.Of((INamespaceSymbol?)null));
        Assert.IsTrue(NamespaceNameIdentity.AreSame("Source.@class", "Source.class"));
        Assert.IsFalse(NamespaceNameIdentity.AreSame("Source.class", "Source"));
    }
}
