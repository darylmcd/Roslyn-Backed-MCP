using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExtractionArgumentRefusalWireTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-EXTRACTION-SENTINEL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task PublicRefusals_ActualServicesPreserveDualEraWireContract(string version)
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("extraction-argument-refusals", version);
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = SampleSolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var root = Path.GetDirectoryName(SampleSolutionPath)!;
        var path = Path.Combine(root, "SampleLib", "RefactoringProbe.cs");
        var cases = CreateCases(id, root, path);
        var observed = new List<(WireCase Case, JsonObject Frame)>();
        foreach (var item in cases)
        {
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(item.Tool, item.Arguments, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            observed.Add((item, frame));
            Console.WriteLine("OBSERVED-WIRE:" + version + ":" + item.Tool + ":" + item.Parameter + ":" + frame.ToJsonString());
        }

        foreach (var (item, frame) in observed)
        {
            Assert.IsNull(frame["error"], frame.ToJsonString());
            var result = frame["result"]!.AsObject();
            Assert.AreEqual(true, result["isError"]!.GetValue<bool>(), frame.ToJsonString());
            if (version == "2026-07-28")
                Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
            else
                Assert.IsFalse(result.ContainsKey("resultType"));
            var content = result["content"]!.AsArray();
            Assert.HasCount(1, content);
            Assert.AreEqual("text", content[0]!["type"]!.GetValue<string>());
            var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
            Assert.AreEqual(true, payload["error"]!.GetValue<bool>());
            Assert.AreEqual("InvalidArgument", payload["category"]!.GetValue<string>());
            Assert.AreEqual(item.Tool, payload["tool"]!.GetValue<string>());
            Assert.AreEqual(item.ExceptionType, payload["exceptionType"]!.GetValue<string>(), frame.ToJsonString());
            StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), item.Tool + "(" + item.Parameter + ":");
            StringAssert.Contains(payload["message"]!.GetValue<string>(), item.Correction);
            Assert.IsFalse(payload.ContainsKey("paramName"));
            Assert.IsFalse(payload.ContainsKey("correlationId"));
            var raw = frame.ToJsonString();
            Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), raw);
            Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(root).Trim('"'), StringComparison.Ordinal), raw);
        }
    }

    private sealed record WireCase(
        string Tool, Dictionary<string, object?> Arguments, string Parameter, string ExceptionType, string Correction);

    private static List<WireCase> CreateCases(string id, string root, string path)
    {
        var cases = new List<WireCase>();
        void Add(string tool, Dictionary<string, object?> arguments, string parameter,
            string correction, string exceptionType = "ArgumentException")
        {
            arguments["workspaceId"] = id;
            cases.Add(new WireCase(tool, arguments, parameter, exceptionType, correction));
        }

        Dictionary<string, object?> TypeArguments() => new()
        {
            ["filePath"] = path,
            ["typeName"] = Sentinel,
            ["memberNames"] = new[] { Sentinel },
            ["newTypeName"] = "Extracted",
        };
        foreach (var name in new string?[] { null, "", "  ", "@", "class", "async", Sentinel + "!" })
        {
            var args = TypeArguments();
            args["newTypeName"] = name;
            Add("extract_type_preview", args, "newTypeName", "valid C# identifier");
        }
        foreach (var members in new string[]?[] { null, [] })
        {
            var args = TypeArguments();
            args["memberNames"] = members;
            Add("extract_type_preview", args, "memberNames",
                members is null ? "memberNames" : "At least one member", members is null ? "ArgumentNullException" : "ArgumentException");
        }

        Add("extract_type_preview",
            new()
            {
                ["filePath"] = path,
                ["typeName"] = "RefactoringProbe",
                ["memberNames"] = new[] { "CalculateArea" },
                ["newTypeName"] = "Extracted",
                ["newFilePath"] = Sentinel + "\0.cs"
            },
            "newFilePath", "Parameter 'newFilePath' is invalid");

        if (OperatingSystem.IsWindows())
            Add("extract_type_preview",
                new()
                {
                    ["filePath"] = path,
                    ["typeName"] = "RefactoringProbe",
                    ["memberNames"] = new[] { "CalculateArea" },
                    ["newTypeName"] = "Extracted",
                    ["newFilePath"] = Sentinel + new string('x', 32768)
                },
                "newFilePath", "Parameter 'newFilePath' is invalid");

        foreach (var parameter in new[] { "recordMetadataName", "newFieldName", "newFieldType" })
        {
            foreach (var value in new string?[] { null, "", "  " })
            {
                var args = new Dictionary<string, object?>
                {
                    ["recordMetadataName"] = "SampleLib.Dog",
                    ["newFieldName"] = Sentinel,
                    ["newFieldType"] = "bool",
                };
                args[parameter] = value;
                Add("preview_record_field_addition", args, parameter,
                    value is null ? parameter : "must not be empty", value is null ? "ArgumentNullException" : "ArgumentException");
            }
        }
        Add("preview_record_field_addition",
            new() { ["recordMetadataName"] = "SampleLib.Dog", ["newFieldName"] = Sentinel, ["newFieldType"] = "bool" },
            "recordMetadataName", "record class or record struct");

        foreach (var parameter in new[] { "typeName", "fromNamespace", "toNamespace" })
        {
            var args = new Dictionary<string, object?>
            {
                ["typeName"] = "Dog",
                ["fromNamespace"] = "SampleLib",
                ["toNamespace"] = "Other",
            };
            args[parameter] = "  ";
            Add("change_type_namespace_preview", args, parameter, "must be provided");
        }
        foreach (var destination in new[]
        {
            Path.Combine(root, "SampleLib2", Sentinel + ".cs"),
            Path.Combine(root, "SampleLib"),
            Path.Combine(root, "SampleLib", "..", Sentinel + ".cs"),
        })
            Add("change_type_namespace_preview",
                new() { ["typeName"] = "Dog", ["fromNamespace"] = "SampleLib", ["toNamespace"] = "Other", ["newFilePath"] = destination },
                "newFilePath", "inside the source project");
        Add("change_type_namespace_preview",
            new() { ["typeName"] = "Dog", ["fromNamespace"] = "SampleLib", ["toNamespace"] = "Other", ["newFilePath"] = Sentinel + "\0.cs" },
            "newFilePath", "Parameter 'newFilePath' is invalid");

        if (OperatingSystem.IsWindows())
            Add("change_type_namespace_preview",
                new()
                {
                    ["typeName"] = "Dog",
                    ["fromNamespace"] = "SampleLib",
                    ["toNamespace"] = "Other",
                    ["newFilePath"] = Sentinel + new string('x', 32768)
                },
                "newFilePath", "Parameter 'newFilePath' is invalid");

        foreach (var shared in new[] { false, true })
        {
            var tool = shared ? "extract_shared_expression_to_helper_preview" : "extract_method_preview";
            var prefix = shared ? "example" : "";
            string Name(string name) => shared ? prefix + char.ToUpperInvariant(name[0]) + name[1..] : name;
            Dictionary<string, object?> Arguments() => new()
            {
                [shared ? "exampleFilePath" : "filePath"] = path,
                [Name("startLine")] = 13,
                [Name("startColumn")] = 9,
                [Name("endLine")] = 15,
                [Name("endColumn")] = 36,
                [shared ? "helperName" : "methodName"] = "Extracted",
            };
            var nameArgs = Arguments();
            nameArgs[shared ? "helperName" : "methodName"] = "  ";
            Add(tool, nameArgs, shared ? "helperName" : "methodName", "must not be empty");
            foreach (var invalidName in new[] { Sentinel + "!", "class", "@" })
            {
                var invalidNameArgs = Arguments();
                invalidNameArgs[shared ? "helperName" : "methodName"] = invalidName;
                Add(tool, invalidNameArgs, shared ? "helperName" : "methodName", "valid C# identifier");
            }
            foreach (var (parameter, value) in new[]
            {
                ("startLine", 0), ("startLine", -1), ("startLine", int.MinValue), ("startLine", int.MaxValue),
                ("endLine", 0), ("endLine", -1), ("endLine", int.MinValue), ("endLine", int.MaxValue),
                ("startColumn", 0), ("startColumn", -1), ("startColumn", int.MinValue), ("startColumn", int.MaxValue),
                ("endColumn", 0), ("endColumn", -1), ("endColumn", int.MinValue), ("endColumn", int.MaxValue),
                ("startColumn", 100), ("endColumn", 100),
            })
            {
                var args = Arguments();
                args[Name(parameter)] = value;
                Add(tool, args, Name(parameter), "must be between 1", "ArgumentOutOfRangeException");
            }
            var reversedLines = Arguments();
            reversedLines[Name("startLine")] = 15;
            reversedLines[Name("endLine")] = 13;
            reversedLines[Name("endColumn")] = 9;
            Add(tool, reversedLines, Name("startLine"), "must not be after");
            var reversedColumns = Arguments();
            reversedColumns[Name("endLine")] = 13;
            reversedColumns[Name("startColumn")] = 10;
            reversedColumns[Name("endColumn")] = 9;
            Add(tool, reversedColumns, Name("startColumn"), "must not be after");
            if (shared)
            {
                var empty = Arguments();
                empty[Name("endLine")] = 13;
                empty[Name("endColumn")] = 9;
                Add(tool, empty, Name("startColumn"), "nonempty complete value expression");
                foreach (var (line, start, end) in new[] { (13, 9, 12), (11, 12, 15), (26, 12, 18), (1, 11, 20), (22, 16, 20), (12, 5, 6) })
                {
                    var typeSelection = Arguments();
                    typeSelection[Name("startLine")] = line;
                    typeSelection[Name("endLine")] = line;
                    typeSelection[Name("startColumn")] = start;
                    typeSelection[Name("endColumn")] = end;
                    Add(tool, typeSelection, Name("startColumn"), "complete value expression");
                }
                var accessibility = Arguments();
                accessibility["helperAccessibility"] = Sentinel;
                Add(tool, accessibility, "helperAccessibility", "private, internal, or public");
            }
        }
        return cases;
    }
    [TestMethod]
    [DataRow(false, "PRIVATE-EXTRACTION-SENTINEL!")]
    [DataRow(false, "class")]
    [DataRow(true, "PRIVATE-EXTRACTION-SENTINEL!")]
    [DataRow(true, "class")]
    public async Task ArgumentRefusals_MethodAndHelperIdentifiersAreValidated(bool shared, string name)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", shared ? "SharedExpressionProbe.cs" : "RefactoringProbe.cs");
        try
        {
            var preview = shared
                ? await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                    workspace.WorkspaceId, path, 18, 13, 18, 104, name, "private", false, CancellationToken.None)
                : await ExtractMethodService.PreviewExtractMethodAsync(
                    workspace.WorkspaceId, path, 13, 9, 15, 36, name, CancellationToken.None);
            var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test-name-probe", CancellationToken.None);
            Assert.IsTrue(applied.Success);
            var source = await File.ReadAllTextAsync(path);
            var errors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
            var compile = await CompileCheckService.CheckAsync(workspace.WorkspaceId, new RoslynMcp.Core.Models.CompileCheckOptions(), CancellationToken.None);
            Console.WriteLine("NAME-PROBE:" + shared + ":" + name + ":parseErrors=" + errors.Length
                + ":inMemoryCompile=" + compile.Success + ":" + string.Join("; ", errors.Select(d => d.ToString())));
            Assert.Fail("Invalid or unescaped keyword name reached preview/apply instead of a caller refusal.");
        }
        catch (RoslynMcp.Core.Services.PublicArgumentException error)
        {
            Assert.AreEqual(shared ? "helperName" : "methodName", error.ParamName);
            StringAssert.Contains(error.PublicMessage, "valid C# identifier");
            Assert.IsFalse(error.PublicMessage.Contains(name, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(false, "async")]
    [DataRow(false, "Äpfel")]
    [DataRow(false, "@class")]
    [DataRow(true, "async")]
    [DataRow(true, "Äpfel")]
    [DataRow(true, "@class")]
    public async Task ArgumentRefusals_MethodAndHelperValidIdentifiersCompile(bool shared, string name)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", shared ? "SharedExpressionProbe.cs" : "RefactoringProbe.cs");
        var preview = shared
            ? await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspace.WorkspaceId, path, 18, 13, 18, 104, name, "private", false, CancellationToken.None)
            : await ExtractMethodService.PreviewExtractMethodAsync(
                workspace.WorkspaceId, path, 13, 9, 15, 36, name, CancellationToken.None);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test-valid-name", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        var source = await File.ReadAllTextAsync(path);
        Assert.IsFalse(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetDiagnostics()
            .Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var compile = await CompileCheckService.CheckAsync(workspace.WorkspaceId, new RoslynMcp.Core.Models.CompileCheckOptions(), CancellationToken.None);
        Assert.IsTrue(compile.Success);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ArgumentRefusals_ContextualAwaitCompilesInAsyncAndCrossFileCallers(bool shared)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ContextualNameHost.cs");
        var otherPath = workspace.GetPath("SampleLib", "OtherContextualNameHost.cs");
        var source = """
            namespace SampleLib;
            public sealed class ContextualNameHost
            {
                public async System.Threading.Tasks.Task<int> Compute(int input)
                {
                    await System.Threading.Tasks.Task.Yield();
                    var value = input * 2;
                    return value;
                }
            }
            """;
        await File.WriteAllTextAsync(path, source, CancellationToken.None);
        await File.WriteAllTextAsync(otherPath, source.Replace("ContextualNameHost", "OtherContextualNameHost"), CancellationToken.None);
        var id = await workspace.LoadAsync(CancellationToken.None);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot();
        var statement = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax>().Single();
        var selection = shared
            ? statement.Declaration.Variables.Single().Initializer!.Value.GetLocation().GetLineSpan()
            : statement.GetLocation().GetLineSpan();
        var preview = shared
            ? await ExtractMethodService.PreviewExtractSharedExpressionToHelperAsync(id, path,
                selection.StartLinePosition.Line + 1, selection.StartLinePosition.Character + 1,
                selection.EndLinePosition.Line + 1, selection.EndLinePosition.Character + 1,
                "await", "public", true, CancellationToken.None)
            : await ExtractMethodService.PreviewExtractMethodAsync(id, path,
                selection.StartLinePosition.Line + 1, selection.StartLinePosition.Character + 1,
                selection.EndLinePosition.Line + 1, selection.EndLinePosition.Character + 1,
                "await", CancellationToken.None);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test-contextual-await", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        var compile = await CompileCheckService.CheckAsync(id, new RoslynMcp.Core.Models.CompileCheckOptions(), CancellationToken.None);
        Console.WriteLine("AWAIT-NAME-PROBE:" + shared + ":compile=" + compile.Success
            + ":" + string.Join("; ", compile.Diagnostics?.Select(d => d.Id + ":" + d.Message) ?? []));
        Assert.IsTrue(compile.Success);
        var output = await File.ReadAllTextAsync(path);
        StringAssert.Contains(output, "@await(");
        var declaration = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(output).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "await");
        Assert.AreEqual("await", declaration.Identifier.ValueText);
        if (shared)
            StringAssert.Contains(await File.ReadAllTextAsync(otherPath), "ContextualNameHost.@await(");
    }

}
