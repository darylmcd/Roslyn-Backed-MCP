using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MetadataMemberSymbolHandleTests
{
    [TestMethod]
    public async Task MetadataMembers_RoundTripExactIdentityAcrossOverloadsAndKinds()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "MetadataMemberConsumer", "MetadataMemberConsumer", LanguageNames.CSharp,
            metadataReferences:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Diagnostics.Process).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SyntaxFactory).Assembly.Location),
            ]));

        var compilation = await solution.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var process = compilation.GetTypeByMetadataName("System.Diagnostics.Process");
        var syntaxFactory = compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.CSharp.SyntaxFactory");
        var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        var stringType = compilation.GetTypeByMetadataName("System.String");
        Assert.IsNotNull(process);
        Assert.IsNotNull(syntaxFactory);
        Assert.IsNotNull(list);
        Assert.IsNotNull(stringType);

        ISymbol[] members =
        [
            process.GetMembers("Kill").OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 0),
            process.GetMembers("Kill").OfType<IMethodSymbol>().Single(m => m.Parameters.Length == 1),
            syntaxFactory.GetMembers("FileScopedNamespaceDeclaration").OfType<IMethodSymbol>()
                .Single(m => m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "NameSyntax"),
            list.GetMembers("Add").OfType<IMethodSymbol>().Single(),
            process.InstanceConstructors.Single(m => m.Parameters.Length == 0),
            process.GetMembers("Id").OfType<IPropertySymbol>().Single(),
            stringType.GetMembers("Empty").OfType<IFieldSymbol>().Single(),
            process.GetMembers("Exited").OfType<IEventSymbol>().Single(),
        ];

        foreach (var member in members)
        {
            var handle = SymbolHandleSerializer.CreateHandle(member);
            var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None);
            Assert.IsNotNull(resolved, $"Failed to resolve {member}.");
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(member, resolved),
                $"Resolved a different member for {member}.");
        }

        var killWithoutTree = SymbolHandleSerializer.CreateHandle(members[0]);
        var killWithTree = SymbolHandleSerializer.CreateHandle(members[1]);
        Assert.AreNotEqual(killWithoutTree, killWithTree);

        var original = SymbolHandleSerializer.ParseHandlePayload(killWithTree);
        Assert.IsNotNull(original.MemberIdentity);
        var wrongAssembly = original with
        {
            MemberIdentity = original.MemberIdentity with { AssemblyIdentity = "WrongAssembly, Version=1.0.0.0" },
        };
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(
            solution, Encode(wrongAssembly), CancellationToken.None));

        var wrongSignature = original with
        {
            MemberIdentity = original.MemberIdentity with { DeclarationId = "M:System.Diagnostics.Process.Kill(System.String)" },
        };
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(
            solution, Encode(wrongSignature), CancellationToken.None));

        var incomplete = original with
        {
            MemberIdentity = original.MemberIdentity with { ContainingTypeMetadataName = "" },
        };
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            SymbolHandleSerializer.ResolveHandleAsync(solution, Encode(incomplete), CancellationToken.None));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            SymbolHandleSerializer.ResolveHandleAsync(solution, killWithTree, new CancellationToken(true)));

        var legacyDisplayOnly = new SymbolHandleSerializer.SymbolHandlePayload(
            null, "System.Diagnostics.Process.Kill(bool)", null, null, null);
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(
            solution, Encode(legacyDisplayOnly), CancellationToken.None));
    }

    private static string Encode(SymbolHandleSerializer.SymbolHandlePayload payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
}
