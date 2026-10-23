using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeWorkspaceArgumentTransportTests
{
    [TestMethod]
    [DataRow("%3B")]
    [DataRow("%3b")]
    [DataRow("%25")]
    [DataRow("%253B")]
    [DataRow("%2525")]
    [DataRow(";")]
    [DataRow(" with space ")]
    [DataRow("plain")]
    public async Task InstalledWorkspace_PreservesActualCompilerArgumentIdentity(string literal)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeWorkspaceArgumentTransportTests), Guid.NewGuid().ToString("N"));
        MSBuildWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            NativeSdkRestoreFixture.InitializeDirectory(root);
            var project = Path.Combine(root, "Transport.csproj");
            var source = Path.Combine(root, "Source.cs");
            var assemblyName = "Native" + literal;
            const string features = "receipt=nonpath\\backslash";
            await File.WriteAllTextAsync(source, "public class Source { }");
            var definition = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("AssemblyName", Escape(assemblyName)), new XElement("Features", Escape(features)),
                    new XElement("EnableDefaultCompileItems", "false"), new XElement("NuGetAudit", "false")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Source.cs"))));
            await File.WriteAllTextAsync(project, definition.ToString(), new UTF8Encoding(false));
            var original = await File.ReadAllBytesAsync(project);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>
            {
                ["UseSharedCompilation"] = "false",
                ["SkipCompilerExecution"] = "true",
                ["ProvideCommandLineArgs"] = "true"
            });
            var runner = new DotnetCommandRunner();
            var restored = await runner.RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], globals), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, restored.StdErr);

            var logger = NativeCscArgumentTransportOracle.Create(root);
            var receipt = Path.Combine(root, "native-workspace-transport.jsonl");
            var native = await runner.RunAsync(root, project, MsBuildCommandProperties.Append(
                ["msbuild", project, "-nologo", "-m:1", "-nr:false", "-noconsolelogger", "-verbosity:diagnostic", "-target:Compile",
                    "-logger:NativeCscArgumentTransportOracleLogger," + logger + ";" + receipt], globals), CancellationToken.None);
            Assert.IsTrue(native.Succeeded, native.StdErr);
            var frames = File.ReadLines(receipt).Select(line => JsonSerializer.Deserialize<Frame>(line)
                ?? throw new AssertFailedException("The actual compiler transport receipt is empty.")).ToArray();
            var actualOutput = frames.Single(frame => frame.Kind == "OutputAssembly").Values.Single();
            var escapedArguments = frames.Single(frame => frame.Kind == "CommandLineArgsEscaped").Values;
            var rawCommand = frames.Single(frame => frame.Kind == "CommandLine").Values.Single();
            var task = NativeCscReferenceArguments.GenerateOutputTransport(actualOutput, source, features, typeof(object).Assembly.Location);
            Assert.IsTrue(rawCommand.StartsWith(task.ToolPath, StringComparison.Ordinal),
                "The actual SDK task must use the independently selected installed compiler host.");
            var operands = Microsoft.CodeAnalysis.CommandLineParser.SplitCommandLineIntoArguments(
                rawCommand[task.ToolPath.Length..], removeHashComments: true).ToArray();
            CollectionAssert.AreEqual(task.HostArguments.ToArray(), operands.Take(task.HostArguments.Length).ToArray());
            var trueArguments = operands.Skip(task.HostArguments.Length).ToArray();
            CollectionAssert.AreEqual(trueArguments, escapedArguments,
                "The native command event must independently qualify the task's escaped compiler-token representation.");
            var parsed = CSharpCommandLineParser.Default.Parse(trueArguments, root, sdkDirectory: null);
            Assert.AreEqual(assemblyName, parsed.CompilationName);
            Assert.AreEqual(assemblyName + ".dll", parsed.CompilationOptions.ModuleName);
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.GetFullPath(actualOutput, root),
                Path.GetFullPath(Path.Combine(parsed.OutputDirectory, parsed.OutputFileName!))));

            // This is the installed upstream consumer itself. The product's independent
            // compiler-context certificate remains unchanged and is not used to repair it.
            workspace = MSBuildWorkspace.Create(globals);
            var semantic = await workspace.OpenProjectAsync(project, cancellationToken: CancellationToken.None);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(project));
            Assert.AreEqual(parsed.CompilationName, semantic.AssemblyName,
                "The installed workspace must retain the actual compiler identity without an additional MSBuild item unescape.");
            Assert.AreEqual(parsed.CompilationOptions.ModuleName, semantic.CompilationOptions!.ModuleName);
            Assert.AreEqual(parsed.ParseOptions.Features["receipt"], ((CSharpParseOptions)semantic.ParseOptions!).Features["receipt"]);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed workspace transport fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed record Frame(string Kind, string[] Values);
    private static string Escape(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);
}
