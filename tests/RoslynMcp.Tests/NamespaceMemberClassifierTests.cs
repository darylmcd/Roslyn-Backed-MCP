using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NamespaceMemberClassifierTests
{
    private static CompilationUnitSyntax Parse(string source) =>
        (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source).GetRoot();

    [TestMethod]
    [DataRow("class Sample { }")]
    [DataRow("struct Sample { }")]
    [DataRow("interface Sample { }")]
    [DataRow("record Sample(int A);")]
    [DataRow("record struct Sample(int A);")]
    [DataRow("enum Sample { A }")]
    [DataRow("delegate void Sample();")]
    public void IsTypeLike_And_IsDirectNamespaceMember_Accept_Every_Named_Type_Kind(string declaration)
    {
        var member = Parse("namespace Old { " + declaration + " }").DescendantNodes()
            .Single(n => NamespaceMemberClassifier.GetTypeLikeName(n) == "Sample");

        Assert.IsTrue(NamespaceMemberClassifier.IsTypeLike(member));
        Assert.IsTrue(NamespaceMemberClassifier.IsDirectNamespaceMember(member));
    }

    [TestMethod]
    public void IsDirectNamespaceMember_Accepts_Compilation_Unit_Level_Types_And_Rejects_Nested_Types()
    {
        var root = Parse("class Outer { enum Inner { A } delegate void Callback(); }");

        var outer = root.DescendantNodes().Single(n => NamespaceMemberClassifier.GetTypeLikeName(n) == "Outer");
        var inner = root.DescendantNodes().Single(n => NamespaceMemberClassifier.GetTypeLikeName(n) == "Inner");
        var callback = root.DescendantNodes().Single(n => NamespaceMemberClassifier.GetTypeLikeName(n) == "Callback");

        Assert.IsTrue(NamespaceMemberClassifier.IsDirectNamespaceMember(outer));
        Assert.IsFalse(NamespaceMemberClassifier.IsDirectNamespaceMember(inner), "a nested enum is a member of its type, not of a namespace");
        Assert.IsFalse(NamespaceMemberClassifier.IsDirectNamespaceMember(callback), "a nested delegate is a member of its type, not of a namespace");
        Assert.IsTrue(NamespaceMemberClassifier.IsTypeLike(inner), "a nested enum is still type-like");
    }

    [TestMethod]
    public void IsTypeLike_Rejects_Namespaces_Members_And_Usings()
    {
        var root = Parse("using System;\nnamespace Old { class Sample { void M() { } int F; } }");

        foreach (var node in root.DescendantNodes().Where(n => n is BaseNamespaceDeclarationSyntax or MethodDeclarationSyntax
                     or FieldDeclarationSyntax or UsingDirectiveSyntax))
        {
            Assert.IsFalse(NamespaceMemberClassifier.IsTypeLike(node), node.Kind().ToString());
            Assert.IsNull(NamespaceMemberClassifier.GetTypeLikeName(node), node.Kind().ToString());
        }
    }

    [TestMethod]
    public void GetContainingNamespaceName_Joins_Nested_Block_And_File_Scoped_Declarations()
    {
        var nested = Parse("namespace Old { namespace Inner.Deep { class Sample { } } }");
        var fileScoped = Parse("namespace Old.File;\nclass Sample { }");
        var global = Parse("class Sample { }");

        string NameOf(CompilationUnitSyntax root) => NamespaceMemberClassifier.GetContainingNamespaceName(
            root.DescendantNodes().Single(n => NamespaceMemberClassifier.GetTypeLikeName(n) == "Sample"));

        Assert.AreEqual("Old.Inner.Deep", NameOf(nested));
        Assert.AreEqual("Old.File", NameOf(fileScoped));
        Assert.AreEqual(string.Empty, NameOf(global));
    }

    [TestMethod]
    [DataRow("namespace Old { enum Keep { A } }", "Old", true)]
    [DataRow("namespace Old { delegate void Keep(); }", "Old", true)]
    [DataRow("namespace Old;\nclass Keep { }", "Old", true)]
    [DataRow("namespace Old { }\nnamespace Old { enum Keep { A } }", "Old", true)]
    [DataRow("namespace Old { namespace Inner { enum Keep { A } } }", "Old", false)]
    [DataRow("namespace Old { namespace Inner { enum Keep { A } } }", "Old.Inner", true)]
    [DataRow("namespace Other { enum Keep { A } }", "Old", false)]
    [DataRow("namespace Old { class Outer { enum Nested { A } } }", "Old.Outer", false)]
    [DataRow("namespace Old { }", "Old", false)]
    public void DeclaresTypeLikeMemberIn_Matches_Only_Direct_Members_Of_The_Named_Namespace(
        string source, string namespaceName, bool expected) =>
        Assert.AreEqual(expected, NamespaceMemberClassifier.DeclaresTypeLikeMemberIn(Parse(source), namespaceName));

    [TestMethod]
    [DataRow("", false, DisplayName = "empty file")]
    [DataRow("using System;\n", false, DisplayName = "usings only")]
    [DataRow("// only a comment\n/// <summary>orphaned doc</summary>\n", false, DisplayName = "comments only")]
    [DataRow("#nullable enable\n", false, DisplayName = "policy directive only")]
    [DataRow("using System;\nnamespace Old { }\n", false, DisplayName = "empty block namespace")]
    [DataRow("namespace Old;\n", false, DisplayName = "empty file-scoped namespace")]
    [DataRow("namespace Old { namespace Inner { } }\n", false, DisplayName = "nested empty namespaces")]
    [DataRow("namespace Old { using System; }\n", false, DisplayName = "namespace using only")]
    [DataRow("#if NEVER\n#endif\n", false, DisplayName = "directives without disabled text")]
    [DataRow("namespace Old { class Keep { } }\n", true, DisplayName = "class")]
    [DataRow("namespace Old { enum Keep { A } }\n", true, DisplayName = "enum")]
    [DataRow("namespace Old { delegate void Keep(); }\n", true, DisplayName = "delegate")]
    [DataRow("namespace Old { namespace Inner { struct Keep { } } }\n", true, DisplayName = "nested namespace type")]
    [DataRow("class Keep { }\n", true, DisplayName = "global namespace type")]
    [DataRow("System.Console.WriteLine();\n", true, DisplayName = "global statement")]
    [DataRow("[assembly: System.CLSCompliant(true)]\n", true, DisplayName = "assembly attribute")]
    [DataRow("[module: System.CLSCompliant(true)]\n", true, DisplayName = "module attribute")]
    [DataRow("namespace Old\n{\n#if NEVER\n    enum Keep { A }\n#endif\n}\n", true, DisplayName = "disabled declaration")]
    [DataRow("namespace Old { class Outer { } }\n", true, DisplayName = "class with members")]
    public void HasRemainingContent_Is_True_Only_When_Something_Besides_Shells_Usings_And_Trivia_Remains(
        string source, bool expected) =>
        Assert.AreEqual(expected, NamespaceMemberClassifier.HasRemainingContent(Parse(source)));
}
