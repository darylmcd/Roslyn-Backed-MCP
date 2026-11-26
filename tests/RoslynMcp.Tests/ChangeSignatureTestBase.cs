using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public abstract class ChangeSignatureTestBase : OwnedRefactoringTestBase
{
    public TestContext TestContext { get; set; } = null!;

    private protected static int AssertSignatureReaderWidth()
    {
        var processors = Environment.ProcessorCount;
        var configured = Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT");
        if (configured is "1" or "4") Assert.AreEqual(int.Parse(configured, System.Globalization.CultureInfo.InvariantCulture), processors);
        var bound = Math.Max(1, Math.Min(4, processors));
        Assert.IsTrue(bound is >= 1 and <= 4);
        return bound;
    }

    private protected static async Task<Array> ReadSignatureOutcomesAsync<T>(
        IReadOnlyList<int> inputs, Func<int, Task<T>> read)
    {
        var method = typeof(ChangeSignatureAddRemovePreviewBuilder).GetMethod("ReadBatchAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        var task = (Task)method.MakeGenericMethod(typeof(int), typeof(T)).Invoke(null, [inputs, read])!;
        await task;
        return (Array)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private protected static async Task<Func<T>[]> ReadSignatureBatchAsync<T>(
        IReadOnlyList<int> inputs, Func<int, Task<T>> read)
    {
        var outcomes = await ReadSignatureOutcomesAsync(inputs, read);
        return outcomes.Cast<object>().Select(outcome => (Func<T>)(() =>
            (T)InvokeSignatureHelper(outcome.GetType().GetMethod("GetResult")!, outcome, null)!)).ToArray();
    }

    private protected static void ConsumeSignatureOutcomes<T>(Array outcomes, Action<T> consume, CancellationToken ct)
    {
        var method = typeof(ChangeSignatureAddRemovePreviewBuilder).GetMethod("ConsumeReadOutcomes",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        InvokeSignatureHelper(method.MakeGenericMethod(typeof(T)), null, [outcomes, consume, ct]);
    }

    private static object? InvokeSignatureHelper(System.Reflection.MethodInfo method, object? owner, object?[]? arguments)
    {
        try { return method.Invoke(owner, arguments); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private protected async Task AssertRepositorySignatureBudgetAsync(string scenario)
    {
        var repository = TestFixtureFileSystem.FindRepositoryRoot();
        // MSTest retains its per-test directory when result attachments reference it.
        // Keep disposable source/build fixtures under the assembly-owned cleanup root.
        var output = Path.Combine(TestContext.TestTempDirectory!, "signature-budget");
        Directory.CreateDirectory(output);
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "signature-budget-" + scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var fixture = Path.Combine(fixtureRoot, "fixture");
        var revision = (await RunBudgetProcessAsync(repository, "git", ["rev-parse", "HEAD"])).Trim();
        await File.WriteAllTextAsync(Path.Combine(output, "fixture-commit.txt"), revision);
        var archive = Path.Combine(fixtureRoot, "fixture.zip");
        await RunBudgetProcessAsync(repository, "git", ["archive", "--format=zip", "--output=" + archive, revision]);
        ZipFile.ExtractToDirectory(archive, fixture);
        var file = Path.Combine(fixture, "src", "RoslynMcp.Roslyn", "Helpers", "PhysicalPathResolver.cs");
        var originalText = await File.ReadAllTextAsync(file);
        if (scenario is "Remove" or "Reorder")
        {
            Assert.AreEqual(1, originalText.Split("CanonicalizeDriveRoot(string pathRoot)", StringSplitOptions.None).Length - 1);
            Assert.AreEqual(2, originalText.Split("CanonicalizeDriveRoot(pathRoot)", StringSplitOptions.None).Length - 1);
            await File.WriteAllTextAsync(file, originalText
                .Replace("CanonicalizeDriveRoot(string pathRoot)", "CanonicalizeDriveRoot(string pathRoot, int latencyMarker)", StringComparison.Ordinal)
                .Replace("CanonicalizeDriveRoot(pathRoot)", "CanonicalizeDriveRoot(pathRoot, 0)", StringComparison.Ordinal));
        }
        var build = await RunBudgetProcessAsync(fixture, "node",
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "scripts", "validation-gate.mjs"),
                "--", "dotnet", "build", "RoslynMcp.slnx", "--nologo"]);
        await File.WriteAllTextAsync(Path.Combine(output, "fixture-build.log"), build);
        var host = typeof(RoslynMcp.Host.Stdio.HostAssemblyMarker).Assembly.Location;
        string hostSha256;
        using (var hostStream = File.OpenRead(host))
            hostSha256 = Convert.ToHexString(SHA256.HashData(hostStream));
        // Setup validation has its own process. The measured process never receives a preview,
        // semantic query, compile check or warm request before the one timed invocation.
        await using (var setup = new SignatureBudgetHost(host, fixture, Path.Combine(output, "setup")))
        {
            var workspace = await setup.LoadAsync();
            var compile = await setup.CallAsync("compile_check", new JsonObject { ["workspaceId"] = workspace });
            Assert.IsFalse(compile["isError"]?.GetValue<bool>() ?? false, compile.ToJsonString());
            var validation = SignatureBudgetHost.Value(compile);
            Assert.IsTrue(validation["success"]!.GetValue<bool>(), validation.ToJsonString());
            Assert.AreEqual(0, validation["errorCount"]!.GetValue<int>(), validation.ToJsonString());
            Assert.AreEqual(0, validation["warningCount"]!.GetValue<int>(), validation.ToJsonString());
            Assert.AreEqual(9, validation["completedProjects"]!.GetValue<int>(), validation.ToJsonString());
            var references = SignatureBudgetHost.Value(await setup.CallAsync("find_references", new JsonObject
            {
                ["workspaceId"] = workspace,
                ["metadataName"] = "RoslynMcp.Roslyn.Helpers.PhysicalPathResolver.CanonicalizeDriveRoot",
            }));
            Assert.AreEqual(2, references["totalCount"]!.GetValue<int>(), references.ToJsonString());
            foreach (var reference in references["items"]!.AsArray())
            {
                var operation = SignatureBudgetHost.Value(await setup.CallAsync("get_operations", new JsonObject
                {
                    ["workspaceId"] = workspace,
                    ["filePath"] = file,
                    ["line"] = reference!["startLine"]!.GetValue<int>(),
                    ["column"] = reference["startColumn"]!.GetValue<int>(),
                }));
                Assert.AreEqual("Invocation", operation["kind"]!.GetValue<string>());
                Assert.AreEqual("string", operation["type"]!.GetValue<string>());
                Assert.AreEqual(scenario is "Remove" or "Reorder" ? "CanonicalizeDriveRoot(pathRoot, 0)" : "CanonicalizeDriveRoot(pathRoot)",
                    operation["syntax"]!.GetValue<string>());
            }
            if (scenario is "Remove" or "Reorder")
            {
                var lines = await File.ReadAllLinesAsync(file);
                var declaration = Array.FindIndex(lines, text => text.Contains("private static string CanonicalizeDriveRoot(", StringComparison.Ordinal));
                var marker = SignatureBudgetHost.Value(await setup.CallAsync("find_references", new JsonObject
                {
                    ["workspaceId"] = workspace,
                    ["filePath"] = file,
                    ["line"] = declaration + 1,
                    ["column"] = lines[declaration].IndexOf("latencyMarker", StringComparison.Ordinal) + 1,
                }));
                Assert.AreEqual(0, marker["totalCount"]!.GetValue<int>(), "The required marker is unused by the unchanged method body.");
            }
        }
        double seconds;
        await using (var measured = new SignatureBudgetHost(host, fixture, Path.Combine(output, "measurement")))
        {
            var workspace = await measured.LoadAsync();
            var source = await File.ReadAllLinesAsync(file);
            var line = Array.FindIndex(source, text => text.Contains("private static string CanonicalizeDriveRoot(", StringComparison.Ordinal));
            Assert.IsTrue(line >= 0);
            var request = new JsonObject
            {
                ["workspaceId"] = workspace,
                ["filePath"] = file,
                ["line"] = line + 1,
                ["column"] = source[line].IndexOf("CanonicalizeDriveRoot", StringComparison.Ordinal) + 1,
                ["op"] = scenario is "Remove" ? "remove" : scenario is "Reorder" ? "reorder" : "add",
            };
            if (scenario is "Remove") request["position"] = 1;
            else if (scenario is "Reorder") request["newOrder"] = "1,0";
            else
            {
                request["name"] = "latencyMarker";
                request["parameterType"] = scenario == "UnknownType" ? "NonexistentLatencyQualificationType" : "int";
            }
            var timer = Stopwatch.StartNew();
            var response = await measured.CallAsync("change_signature_preview", request);
            seconds = timer.Elapsed.TotalSeconds;
            var capture = new JsonObject
            {
                ["fixtureCommit"] = revision,
                ["hostSha256"] = hostSha256,
                ["scenario"] = scenario,
                ["seconds"] = seconds,
                ["request"] = request.DeepClone(),
                ["response"] = response.DeepClone(),
            };
            var capturePath = Path.Combine(output, "timing.json");
            await File.WriteAllTextAsync(capturePath, capture.ToJsonString(new() { WriteIndented = true }));
            TestContext.AddResultFile(capturePath);
            var value = SignatureBudgetHost.Value(response);
            if (scenario == "UnknownType")
            {
                Assert.IsTrue(response["isError"]!.GetValue<bool>());
                Assert.AreEqual("InvalidOperation", value["category"]!.GetValue<string>());
                Assert.AreEqual("The requested signature change introduces compiler diagnostic CS0246. Retain declaration grammar, parameter dependencies, and valid constructor initialization.", value["message"]!.GetValue<string>());
                Assert.IsNull(value["previewToken"]);
            }
            else
            {
                Assert.IsFalse(response["isError"]?.GetValue<bool>() ?? false, response.ToJsonString());
                Assert.IsFalse(string.IsNullOrWhiteSpace(value["previewToken"]!.GetValue<string>()));
                Assert.IsTrue(value["changes"]!.AsArray().Count > 0);
                Assert.AreEqual(2, value["callsiteUpdates"]!.AsArray().Sum(update => update!["callsiteCount"]!.GetValue<int>()));
            }
        }
        foreach (var evidenceFile in new[] { "fixture-build.log", "fixture-commit.txt",
            "setup/frames.jsonl", "setup/stderr.log", "setup/exit.json",
            "measurement/frames.jsonl", "measurement/stderr.log", "measurement/exit.json" })
            TestContext.AddResultFile(Path.Combine(output, evidenceFile));
        TestContext.WriteLine($"{scenario} took {seconds:F6}s with host SHA-256 {hostSha256} and fixture commit {revision}. Evidence at {output}.");
    }

    private static async Task<string> RunBudgetProcessAsync(string directory, string executable, string[] arguments)
    {
        var start = DotnetCommandRunner.CreateStartInfo(directory, arguments, executable);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(6));
            var result = await stdout + await stderr;
            Assert.AreEqual(0, process.ExitCode, result);
            return result;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private sealed class SignatureBudgetHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _fixture;
        private readonly StreamWriter _frames;
        private readonly string _directory;
        private readonly Task<string> _stderr;
        private int _id;

        public SignatureBudgetHost(string host, string fixture, string directory)
        {
            Directory.CreateDirectory(directory);
            _fixture = fixture;
            _directory = directory;
            _frames = File.CreateText(Path.Combine(directory, "frames.jsonl"));
            var start = DotnetCommandRunner.CreateStartInfo(fixture, [host]);
            start.Environment["ROSLYNMCP_SANCTIONED_ROOTS"] = fixture;
            start.Environment["ROSLYNMCP_OBSERVABILITY_SINK"] = "disabled";
            start.Environment["XDG_STATE_HOME"] = Path.Combine(directory, "state");
            _process = Process.Start(start)!;
            _stderr = _process.StandardError.ReadToEndAsync();
        }

        public async Task<string> LoadAsync()
        {
            await RpcAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "signature-budget", ["version"] = "1" },
            });
            await _process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await _process.StandardInput.FlushAsync();
            var info = Value(await CallAsync("server_info", new JsonObject()));
            Assert.AreEqual("5.12.0.0", info["roslynVersion"]!.GetValue<string>());
            Assert.IsTrue(info["pathBoundary"]!["enforcing"]!.GetValue<bool>());
            Assert.IsFalse(info["pathBoundary"]!["failOpen"]!.GetValue<bool>());
            Assert.AreEqual(1, info["pathBoundary"]!["configuredRootCount"]!.GetValue<int>());
            var load = await CallAsync("workspace_load", new JsonObject { ["path"] = Path.Combine(_fixture, "RoslynMcp.slnx") });
            Assert.IsFalse(load["isError"]?.GetValue<bool>() ?? false, load.ToJsonString());
            var state = Value(load);
            Assert.IsTrue(state["isReady"]!.GetValue<bool>(), state.ToJsonString());
            Assert.IsTrue(state["analyzersReady"]!.GetValue<bool>(), state.ToJsonString());
            return state["workspaceId"]!.GetValue<string>();
        }

        public Task<JsonNode> CallAsync(string name, JsonObject arguments) =>
            RpcAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments.DeepClone() });

        private async Task<JsonNode> RpcAsync(string method, JsonObject parameters)
        {
            var id = ++_id;
            var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
            await _frames.WriteLineAsync(request.ToJsonString());
            await _process.StandardInput.WriteLineAsync(request.ToJsonString());
            await _process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(6));
                Assert.IsNotNull(line, "Owned host exited before responding.");
                await _frames.WriteLineAsync(line);
                await _frames.FlushAsync();
                var response = JsonNode.Parse(line)!;
                if (response["id"]?.GetValue<int>() != id) continue;
                Assert.IsNull(response["error"], response.ToJsonString());
                return response["result"]!.DeepClone();
            }
        }

        public static JsonNode Value(JsonNode response) => response["structuredContent"]?.DeepClone()
            ?? JsonNode.Parse(response["content"]![0]!["text"]!.GetValue<string>())!;

        public async ValueTask DisposeAsync()
        {
            var forced = false;
            _process.StandardInput.Close();
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { forced = true; _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(); }
            var exitCode = _process.ExitCode;
            await File.WriteAllTextAsync(Path.Combine(_directory, "exit.json"), new JsonObject
            {
                ["pid"] = _process.Id,
                ["exitCode"] = exitCode,
                ["forced"] = forced,
            }.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(_directory, "stderr.log"), await _stderr);
            await _frames.DisposeAsync();
            _process.Dispose();
            Assert.IsFalse(forced, "Owned measured/setup host must finish after stdin closes.");
            Assert.AreEqual(0, exitCode, "Owned measured/setup host must exit successfully.");
        }
    }

    private ChangeSignatureService? _changeSignatureService;
    private protected async Task<(IsolatedWorkspaceTestBase.IsolatedWorkspaceScope Workspace, string TargetPath,
        string FactoryPath, string Source, string FactorySource)> CreateLateAliasFixtureAsync(bool diagnostic, bool exchange)
    {
        var workspace = CreateSingleProjectWorkspace();
        try
        {
            var targetPath = workspace.GetPath("SampleLib", "LateAliasTarget.cs");
            var factoryPath = workspace.GetPath("SampleLib", "AliasFactory.cs");
            var source = "using first = System.String;\nnamespace SampleLib;\npublic static class SignatureProof { "
                + "public static int Compute<T>(T missing, int retained) => "
                + (exchange ? "missing is null ? retained : retained" : "retained")
                + "; public static string Run() => Compute("
                + (exchange ? "missing ? AliasFactory.Create() : AliasFactory.Create()" : "AliasFactory.Create()")
                + ", 1) + \"|\" + first.Empty + nameof(first); }";
            var factorySource = "extern alias first;\nnamespace SampleLib;\npublic static class AliasFactory { "
                + "public static first::Shared.Value Create() => new first::Shared.Value(); "
                + "public static string Unrelated() => string.Concat(\"a\", \"b\"); "
                + "public static System.Func<string> Reference => Unrelated; "
                + (diagnostic ? "public static MissingAliasHostType? Missing; " : "") + "}";
            await File.WriteAllTextAsync(targetPath, source);
            await File.WriteAllTextAsync(factoryPath, factorySource);
            AddSignatureAliasReference(workspace, "AliasFirst", "first");
            await workspace.LoadAsync(CancellationToken.None);
            return (workspace, targetPath, factoryPath, source, factorySource);
        }
        catch
        {
            await workspace.DisposeAsync();
            throw;
        }
    }

    private protected static void AddSignatureAliasReference(
        IsolatedWorkspaceTestBase.IsolatedWorkspaceScope workspace, string assemblyName, string aliasName)
    {
        var dotnetRoot = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
        var targetFramework = "net" + AppContext.TargetFrameworkName!.Split("Version=v", StringSplitOptions.None)[1];
        var referenceDirectory = Directory.GetDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path).Split('-')[0]))
            .Select(path => Path.Combine(path, "ref", targetFramework)).First(Directory.Exists);
        var alias = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText("namespace Shared; public struct Value { }")],
            Directory.GetFiles(referenceDirectory, "*.dll").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var image = workspace.GetPath("SampleLib", assemblyName + ".dll");
        using (var stream = File.Create(image))
        {
            var emission = alias.Emit(stream);
            Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
        }
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
        projectXml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", assemblyName),
                new System.Xml.Linq.XElement("HintPath", assemblyName + ".dll"),
                new System.Xml.Linq.XElement("Aliases", aliasName))));
        projectXml.Save(projectPath);
    }

    private protected static async Task AssertLateAliasFactoryBindingsAsync(Solution original, Solution candidate, string path)
    {
        static async Task<string[]> BindingsAsync(Solution solution, string path)
        {
            var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(path).Single())!;
            var root = (await document.GetSyntaxRootAsync())!;
            var model = (await document.GetSemanticModelAsync())!;
            return root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>()
                .Where(node => node is Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax
                    or Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax || node.ToString() == "Unrelated")
                .Select(node =>
                {
                    var target = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
                    Assert.IsNotNull(target, node.ToString());
                    var types = model.GetTypeInfo(node);
                    return target.ToDisplayString() + "|" + target.ContainingAssembly.Name + "|"
                        + string.Join(",", target.TypeArguments.Select(t => t.ToDisplayString())) + "|"
                        + types.Type?.ToDisplayString() + "|" + types.ConvertedType?.ToDisplayString()
                        + "|" + model.GetConversion(node);
                }).ToArray();
        }
        var before = await BindingsAsync(original, path);
        Assert.IsTrue(before.Length >= 3, "Construction, unrelated invocation and method reference must all be recorded.");
        CollectionAssert.AreEqual(before, await BindingsAsync(candidate, path));
    }

    private protected ChangeSignatureService ChangeSignature => _changeSignatureService ??= new(
        Services.WorkspaceManager, Services.PreviewStore, Services.RefactoringService);
    private protected static async Task<string> RunSignatureFixtureAsync(
            Microsoft.CodeAnalysis.Solution solution, string typeName)
    {
        var project = solution.Projects.Single(p => p.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics.OrderByDescending(d => d.Severity)));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("signature-proof", isCollectible: true);
        context.Resolving += (loader, name) =>
        {
            var reference = compilation.References.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
                .FirstOrDefault(r => r.FilePath is { } file && File.Exists(file)
                    && System.Reflection.AssemblyName.GetAssemblyName(file).Name == name.Name);
            if (reference?.FilePath is not { } dependency) return null;
            using var dependencyImage = new MemoryStream(File.ReadAllBytes(dependency));
            return loader.LoadFromStream(dependencyImage);
        };
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType(typeName, throwOnError: true)!.GetMethod("Run");
            Assert.IsNotNull(method);
            return Convert.ToString(method.Invoke(null, null), System.Globalization.CultureInfo.InvariantCulture)!;
        }
        finally
        {
            context.Unload();
        }
    }
    private protected async Task AssertSignatureProofAsync(
            string source, ChangeSignatureRequest request, int line, int column, string? expected, int? expectedCalls)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var expectedValue = expected ?? originalValue;
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, line, column), request, CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        if (expectedCalls is not null)
            Assert.AreEqual(expectedCalls, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var proofText = (await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib")
            .Documents.Single(d => d.FilePath == path).GetTextAsync()).ToString();
        foreach (var marker in new[] { "init-comma", "iterator-comma", "receiver", "question", "dot", "name", "arrow", "semicolon" })
        {
            var comment = "/*signature-" + marker + "*/";
            Assert.AreEqual(source.Split(comment, StringSplitOptions.None).Length - 1,
                proofText.Split(comment, StringSplitOptions.None).Length - 1, comment);
        }
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(
            Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }
    private protected sealed class SignatureStoreProbe(IPreviewStore inner) : IPreviewStore
    {
        public int StoreCalls { get; private set; }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes);
        }

        public (string WorkspaceId, Microsoft.CodeAnalysis.Solution OriginalSolution, Microsoft.CodeAnalysis.Solution ModifiedSolution,
            int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => inner.Retrieve(token);

        public void Invalidate(string token) => inner.Invalidate(token);
        public void InvalidateAll(string? workspaceId = null) => inner.InvalidateAll(workspaceId);
        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => inner.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion);
        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
    }
    private protected static async Task AssertOptionalArgumentBindingsAsync(
            Microsoft.CodeAnalysis.Solution original, Microsoft.CodeAnalysis.Solution candidate, string path)
    {
        static async Task<Microsoft.CodeAnalysis.Operations.IInvocationOperation> ComputeOperationAsync(
            Microsoft.CodeAnalysis.Solution solution, string file)
        {
            var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(file).Single())!;
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            Assert.IsNotNull(root);
            Assert.IsNotNull(model);
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString() == "Compute");
            var operation = model.GetOperation(call) as Microsoft.CodeAnalysis.Operations.IInvocationOperation;
            Assert.IsNotNull(operation);
            return operation;
        }
        var before = await ComputeOperationAsync(original, path);
        var after = await ComputeOperationAsync(candidate, path);
        Assert.AreEqual(before.TargetMethod.ContainingType.Name, after.TargetMethod.ContainingType.Name);
        Assert.AreEqual(before.TargetMethod.Name, after.TargetMethod.Name);
        foreach (var argument in before.Arguments)
        {
            Assert.IsNotNull(argument.Parameter);
            var retained = after.Arguments.Single(a => a.Parameter?.Name == argument.Parameter.Name);
            Assert.IsNotNull(retained.Parameter);
            Assert.AreEqual(argument.Parameter.Type.ToDisplayString(), retained.Parameter.Type.ToDisplayString());
            Assert.IsTrue(argument.Value.ConstantValue.HasValue);
            Assert.IsTrue(retained.Value.ConstantValue.HasValue);
            Assert.AreEqual(argument.Value.ConstantValue.Value, retained.Value.ConstantValue.Value,
                $"original binding for {argument.Parameter.Name} must retain its value");
            Assert.AreEqual(argument.InConversion.IsIdentity, retained.InConversion.IsIdentity);
        }
    }
}
