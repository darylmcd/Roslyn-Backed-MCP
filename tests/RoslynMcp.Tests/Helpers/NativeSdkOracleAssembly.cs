using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Compiles the owned native task/logger oracles against the actual installed MSBuild contract.</summary>
internal static class NativeSdkOracleAssembly
{
    internal static string Create(string root, string assemblyName, string source)
    {
        var path = Path.Combine(root, assemblyName + ".dll");
        var sdk = BuildInputProvenance.RegisteredSdkDirectory();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Distinct(FileSystemPath.Comparer)
            .ToDictionary(value => Path.GetFileName(value)!, value => value, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Microsoft.Build.Framework.dll", "Microsoft.Build.Utilities.Core.dll" })
            references[name] = Path.Combine(sdk, name);
        var compilation = CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source)],
            references.Values.Select(value => MetadataReference.CreateFromFile(value)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitted = compilation.Emit(path);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return path;
    }
}
