using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ReceivingNamespaceFactoryTests
{
    [TestMethod]
    [DataRow(LanguageVersion.CSharp7_3, false)]
    [DataRow(LanguageVersion.CSharp9, false)]
    [DataRow(LanguageVersion.CSharp10, true)]
    [DataRow(LanguageVersion.CSharp14, true)]
    [DataRow(LanguageVersion.Default, true)]
    [DataRow(LanguageVersion.Latest, true)]
    [DataRow(LanguageVersion.LatestMajor, true)]
    [DataRow(LanguageVersion.Preview, true)]
    public void Create_Uses_Effective_Receiving_Language(LanguageVersion version, bool fileScoped)
    {
        var options = new CSharpParseOptions(version);
        Assert.AreSame(options, ReceivingNamespaceFactory.RequireCSharpOptions(options));
        var result = ReceivingNamespaceFactory.Create("Receiving.Contracts", options)!;
        Assert.AreEqual(fileScoped, result is FileScopedNamespaceDeclarationSyntax);
        var member = SyntaxFactory.ClassDeclaration("Contract");
        var text = result.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(member)).NormalizeWhitespace().ToFullString();
        var parsed = CSharpSyntaxTree.ParseText(text, options);
        Assert.AreEqual(0, parsed.GetDiagnostics().Count(), text);
        Assert.AreEqual(version, options.SpecifiedLanguageVersion);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    public void Create_Leaves_Global_Namespace_Unwrapped(string name) =>
        Assert.IsNull(ReceivingNamespaceFactory.Create(name, new CSharpParseOptions(LanguageVersion.CSharp9)));

    [TestMethod]
    public void RequireCSharpOptions_Refuses_Missing_Policy()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => ReceivingNamespaceFactory.RequireCSharpOptions(null));
        Assert.IsFalse(error is PublicInvalidOperationException);
    }

    [TestMethod]
    public void Create_Requires_Options_Even_For_Global_Namespace() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => ReceivingNamespaceFactory.Create("", null!));
}
