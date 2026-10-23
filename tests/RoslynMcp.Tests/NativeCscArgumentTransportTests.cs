using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCscArgumentTransportTests
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
    public async Task ActualInstalledCsc_ArgumentReceiptMatchesTrueCompilerArguments(string literal)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCscArgumentTransportTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Transport.csproj");
            var source = Path.Combine(root, "Source.cs");
            var output = Path.Combine(root, "Output" + literal + ".dll");
            var reference = typeof(object).Assembly.Location;
            const string features = "receipt=nonpath\\backslash";
            await File.WriteAllTextAsync(source, "public class Source { }");
            var sdk = BuildInputProvenance.RegisteredSdkDirectory();
            var xml = new XElement("Project", new XElement("UsingTask",
                new XAttribute("TaskName", "Microsoft.CodeAnalysis.BuildTasks.Csc"),
                new XAttribute("AssemblyFile", Path.Combine(sdk, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"))),
                new XElement("Target", new XAttribute("Name", "ObserveCompiler"),
                    new XElement("Csc", new XAttribute("Sources", Escape(source)), new XAttribute("OutputAssembly", Escape(output)),
                        new XAttribute("References", Escape(reference)), new XAttribute("NoStandardLib", "true"), new XAttribute("TargetType", "library"),
                        new XAttribute("Features", Escape(features)), new XAttribute("ProvideCommandLineArgs", "true"),
                        new XAttribute("SkipCompilerExecution", "true"),
                        new XElement("Output", new XAttribute("TaskParameter", "CommandLineArgs"),
                            new XAttribute("ItemName", "ObservedCscCommandLineArgs")))));
            await File.WriteAllTextAsync(project, xml.ToString(), new UTF8Encoding(false));
            var oracle = NativeCscReferenceArguments.GenerateOutputTransport(output, source, features, reference);
            var parsed = CSharpCommandLineParser.Default.Parse(oracle.UnadjustedArguments, root, sdkDirectory: null);
            Assert.AreEqual(output, Path.GetFullPath(Path.Combine(parsed.OutputDirectory, parsed.OutputFileName!)));
            Assert.AreEqual("nonpath\\backslash", parsed.ParseOptions.Features["receipt"],
                "The installed task's true compiler argument grammar must retain non-path backslashes before transport.");

            var logger = NativeCscArgumentTransportOracle.Create(root);
            var receipt = Path.Combine(root, "native-transport.jsonl");
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var arguments = MsBuildCommandProperties.Append(["msbuild", project, "-nologo", "-m:1", "-nr:false", "-noconsolelogger",
                "-verbosity:diagnostic", "-target:ObserveCompiler", "-logger:NativeCscArgumentTransportOracleLogger," + logger + ";" + receipt], globals);
            var native = await new DotnetCommandRunner().RunAsync(root, project, arguments, CancellationToken.None);
            Assert.IsTrue(native.Succeeded, native.StdErr);
            var frames = File.ReadLines(receipt).Select(line => JsonSerializer.Deserialize<Frame>(line)
                ?? throw new AssertFailedException("The native transport oracle returned an empty frame.")).ToArray();
            var actualOutput = frames.Single(frame => frame.Kind == "OutputAssembly").Values.Single();
            Assert.AreEqual(output, actualOutput, "The real Csc task must receive the exact literal output identity.");
            CollectionAssert.AreEqual(new[] { reference }, frames.Single(frame => frame.Kind == "References").Values,
                "The actual compiler must receive the real reference needed for a complete native receipt.");
            var actualItems = frames.Single(frame => frame.Kind == "CommandLineArgs").Values;
            var escapedItems = frames.Single(frame => frame.Kind == "CommandLineArgsEscaped").Values;
            CollectionAssert.AreEqual(oracle.ItemSpecs.ToArray(), actualItems,
                "The actual installed task's output must agree with its independently called argument-item factory.");
            CollectionAssert.AreEqual(oracle.EscapedIncludes.ToArray(), escapedItems);
            var rawCommand = frames.Single(frame => frame.Kind == "CommandLine").Values.Single();
            Assert.AreEqual(oracle.LoggedCommand, rawCommand,
                "The actual command event must qualify the selected task's complete host/command/response grammar, including spacing.");
            Assert.IsTrue(rawCommand.StartsWith(oracle.ToolPath, StringComparison.Ordinal));
            var nativeOperands = Microsoft.CodeAnalysis.CommandLineParser.SplitCommandLineIntoArguments(
                rawCommand[oracle.ToolPath.Length..], removeHashComments: true).ToArray();
            CollectionAssert.AreEqual(oracle.HostArguments.ToArray(), nativeOperands.Take(oracle.HostArguments.Length).ToArray(),
                "Remove only the exact installed native host prefix; the raw executable path need not be quoted.");
            var nativeArguments = nativeOperands.Skip(oracle.HostArguments.Length).ToArray();
            CollectionAssert.AreEqual(oracle.Arguments.ToArray(), nativeArguments);
            CollectionAssert.AreEqual(nativeArguments, escapedItems,
                "The native escaped task output must preserve the actual adjusted compiler operands without another item unescape.");
            var actualParsed = CSharpCommandLineParser.Default.Parse(nativeArguments, root, sdkDirectory: null);
            Assert.AreEqual(output, Path.GetFullPath(Path.Combine(actualParsed.OutputDirectory, actualParsed.OutputFileName!)));
            var adjustedParsed = CSharpCommandLineParser.Default.Parse(oracle.Arguments, root, sdkDirectory: null);
            Assert.AreEqual(adjustedParsed.ParseOptions.Features["receipt"], actualParsed.ParseOptions.Features["receipt"]);
            Console.WriteLine("Native Csc transport qualification: trueArgs=" + oracle.Arguments.Length
                + "; itemArgs=" + actualItems.Length + "; rawCommandEvents=" + frames.Count(frame => frame.Kind == "CommandLine")
                + "; escapedEqualsTrue=" + oracle.Arguments.SequenceEqual(escapedItems, StringComparer.Ordinal));

            var (product, _) = await NativeCompilerTaskInputs.ReadRawAsync(project, globals, null, "Current", ["ObserveCompiler"],
                NativeCompilerTaskInputs.CapturePurpose.FinalCompiler, CancellationToken.None);
            Assert.AreEqual(output, product.Output);
            Assert.HasCount(1, product.References);
            Assert.AreEqual(reference, product.References[0].Path);
            CollectionAssert.AreEqual(oracle.Arguments.ToArray(), product.Arguments.ToArray(),
                "The product receipt must describe the actual compiler command, not an MSBuild item interpretation of those strings.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native argument transport fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed record Frame(string Kind, string[] Values);
    private static string Escape(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);
}
