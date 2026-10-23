using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nito.AsyncEx;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Actual native analyzer reader proof shared by session-replacement route controls.</summary>
internal static class NativeAnalyzerReaderLifetime
{
    internal sealed record Images(string GeneratorPath, string DependencyName);

    internal static async Task<Images> CreateImagesAsync(string directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var dependencyName = "LateReaderDependency" + Guid.NewGuid().ToString("N");
        var generatorName = "LateReaderGenerator" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(Path.Combine(directory, dependencyName + ".dll"), Emit(dependencyName,
            "public static class LateDependencyValue { public static string Marker => \"LateDependencyMarker\"; }", ct), ct);
        var generatorPath = Path.Combine(directory, generatorName + ".dll");
        await File.WriteAllBytesAsync(generatorPath, Emit(generatorName, $$"""
            using Microsoft.CodeAnalysis;
            [Generator] public sealed class LateReaderGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context)
                {
                    var loader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(GetType().Assembly);
                    var dependency = loader.LoadFromAssemblyName(new System.Reflection.AssemblyName("{{dependencyName}}"));
                    var marker = dependency.GetType("LateDependencyValue").GetProperty("Marker").GetValue(null);
                    context.AddSource("LateDependency.g.cs", "public sealed class " + marker + " { }");
                }
            }
            """, ct), ct);
        ct.ThrowIfCancellationRequested();
        return new(generatorPath, dependencyName);
    }

    internal static Task<bool> RunReaderAsync(WorkspaceExecutionGate gate, IWorkspaceManager manager, string workspaceId,
        Images images, TaskCompletionSource entered, Task release, CancellationToken cancellationToken)
        => gate.RunReadAsync(workspaceId, async ct =>
        {
            var snapshot = manager.GetCurrentSolution(workspaceId);
            _ = NativeCompilerContextCertificate.Require(snapshot);
            var project = snapshot.Projects.Single(item => item.AnalyzerReferences.Any(reference =>
                FileSystemPath.Comparer.Equals(reference.FullPath, images.GeneratorPath)));
            var reference = project.AnalyzerReferences.Single(item =>
                FileSystemPath.Comparer.Equals(item.FullPath, images.GeneratorPath));
            var generator = reference.GetGenerators(LanguageNames.CSharp).Single();
            var context = AssemblyLoadContext.GetLoadContext(generator.GetType().Assembly);
            Assert.IsNotNull(context);
            Assert.IsTrue(context.IsCollectible, "This must be the workspace-owned loader retired by reload.");
            Assert.IsFalse(context.Assemblies.Any(assembly => assembly.GetName().Name == images.DependencyName),
                "The dependency must remain unloaded until the reader resumes its actual generator execution.");
            entered.TrySetResult();
            await release.WaitAsync(ct);
            var compilation = CSharpCompilation.Create("HeldReader", references:
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics, ct);
            Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                || diagnostic.Id is "CS8784" or "CS8785"), string.Join("\n", diagnostics));
            Assert.IsNotNull(generated.GetTypeByMetadataName("LateDependencyMarker"),
                "The admitted reader must retain its executable dependency images through actual generator execution.");
            return true;
        }, cancellationToken);

    internal static async Task AwaitEnteredOrFailureAsync(Task entered, Task reader)
    {
        await Task.WhenAny(entered, reader);
        if (!entered.IsCompleted) await reader;
        await entered;
    }

    internal static bool HasWaitingWriter(WorkspaceExecutionGate gate, string workspaceId)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var registryField = typeof(WorkspaceExecutionGate).GetField("_rwLocks", flags);
        Assert.IsNotNull(registryField, "The exact production lock registry must remain observable.");
        var registry = Assert.IsInstanceOfType<AsyncReaderWriterLockRegistry>(registryField.GetValue(gate));
        var rwLock = registry.Get(workspaceId);
        var mutexField = typeof(AsyncReaderWriterLock).GetField("_mutex", flags);
        var queueField = typeof(AsyncReaderWriterLock).GetField("_writerQueue", flags);
        Assert.IsNotNull(mutexField, "The installed Nito mutex protocol must match this qualification.");
        Assert.IsNotNull(queueField, "The installed Nito writer-queue protocol must match this qualification.");
        var mutex = mutexField.GetValue(rwLock);
        Assert.IsNotNull(mutex);
        var queueType = queueField.FieldType;
        Assert.IsTrue(queueType.IsGenericType);
        Assert.AreEqual("Nito.AsyncEx.IAsyncWaitQueue`1", queueType.GetGenericTypeDefinition().FullName);
        CollectionAssert.AreEqual(new[] { typeof(IDisposable) }, queueType.GenericTypeArguments);
        var queue = queueField.GetValue(rwLock);
        Assert.IsNotNull(queue);
        Assert.IsTrue(queueType.IsInstanceOfType(queue));
        var isEmpty = queueType.GetProperty("IsEmpty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(isEmpty, "The installed queue must expose its exact read-only emptiness contract.");
        Assert.AreEqual(typeof(bool), isEmpty.PropertyType);
        Assert.IsTrue(isEmpty.CanRead);
        Assert.HasCount(0, isEmpty.GetIndexParameters());
        lock (mutex) return !Assert.IsInstanceOfType<bool>(isEmpty.GetValue(queue));
    }

    private static byte[] Emit(string name, string source, CancellationToken ct)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.Loader.dll")),
                MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: ct);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        return output.ToArray();
    }
}
