using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectRefactoringIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Extract_Interface_Preview_And_Apply_Creates_Interface_File_And_Project_Reference()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);
        Assert.IsTrue(sourceContents.Contains("IAnimalService", StringComparison.Ordinal));

        var projectXml = XDocument.Load(sourceProjectFilePath);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "Contracts.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Extract_Interface_Apply_Writes_Properly_Indented_ItemGroup_For_New_Project_Reference()
    {
        // Regression for refactoringservice-god-class-decomposition (item 1): RefactoringService
        // previously carried a private GetOrCreateItemGroup that appended a fresh <ItemGroup> via
        // `document.Root?.Add(itemGroup)` with NO trivia, producing collapsed on-disk XML —
        // `...</PropertyGroup>\n<ItemGroup>...</ItemGroup></Project>` with the ItemGroup at column 0
        // and `</Project>` glued onto `</ItemGroup>`. The consolidated
        // OrchestrationMsBuildXml.GetOrCreateItemGroup splices the new element with matching
        // indent + line-ending trivia (AppendRootChildWithFormatting). SampleLib.csproj ships with
        // NO existing ProjectReference ItemGroup, so this apply exercises the new-ItemGroup path.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var rawProjectXml = await File.ReadAllTextAsync(sourceProjectFilePath, CancellationToken.None);
        var normalized = rawProjectXml.Replace("\r\n", "\n", StringComparison.Ordinal);

        // (1) The freshly-created ItemGroup must be on its own indented line, not appended at
        //     column 0 as the private-dup helper did.
        StringAssert.Contains(
            normalized,
            "\n  <ItemGroup>",
            $"New ProjectReference ItemGroup must be spliced on its own indented line.\nFile contents:\n{rawProjectXml}");

        // (2) The closing </ItemGroup> must not be glued directly onto </Project> — the latent
        //     formatting bug the private dup lacked a fix for.
        Assert.IsFalse(
            normalized.Contains("</ItemGroup></Project>", StringComparison.Ordinal),
            $"Closing </ItemGroup> must not be glued onto </Project>.\nFile contents:\n{rawProjectXml}");

        // (3) The reference itself must be present and target the new project.
        var projectXml = XDocument.Parse(rawProjectXml);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "Contracts.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Move_Type_To_Project_Preview_And_Apply_Moves_File_And_Adds_Project_Reference()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "AnimalsShared", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var targetFilePath = workspace.GetPath("AnimalsShared", "Dog.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "Dog",
            "AnimalsShared",
            null,
            CancellationToken.None,
            preserveNamespace: false);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(sourceFilePath));
        Assert.IsTrue(File.Exists(targetFilePath));

        var movedText = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(movedText, "namespace AnimalsShared");

        var projectXml = XDocument.Load(sourceProjectFilePath);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "AnimalsShared.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Move_Type_To_Project_PreserveNamespace_Keeps_Source_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "AnimalsShared", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var targetFilePath = workspace.GetPath("AnimalsShared", "Dog.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "Dog",
            "AnimalsShared",
            null,
            CancellationToken.None,
            preserveNamespace: true);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var movedText = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(movedText, "namespace SampleLib");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Preview_And_Apply_Rewrites_Constructor_Parameters()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var consumerFilePath = workspace.GetPath("SampleApp", "AnimalCoordinator.cs");
        File.WriteAllText(
            consumerFilePath,
            "using SampleLib;\n\npublic class AnimalCoordinator\n{\n    public AnimalCoordinator(AnimalService service)\n    {\n    }\n}\n");

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
        Assert.IsTrue(consumerContents.Contains("IAnimalService", StringComparison.Ordinal));
        // Constructor parameter must now declare the interface, not the concrete class. Use a
        // word-boundary-aware check so `IAnimalService service` (the desired post-fix output)
        // doesn't trip a naive "AnimalService service" substring match.
        Assert.IsFalse(
            consumerContents.Contains("(AnimalService service", StringComparison.Ordinal),
            $"Constructor still declares concrete type.\nConsumer contents:\n{consumerContents}");
        StringAssert.Contains(consumerContents, "IAnimalService service");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Preview_Appends_Interface_WithTrailingCommaFormatting()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "JobQueue.cs");
        File.WriteAllText(
            sourceFilePath,
            """
            namespace SampleLib;

            public sealed record JobRequest(string Name);

            public interface IJobQueue<T>
            {
                void Enqueue(T request);
            }

            public class JobQueue : IJobQueue<JobRequest>
            {
                public void Enqueue(JobRequest request)
                {
                }
            }
            """);

        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "JobQueue",
            "IJobQueueInverted",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);
        var normalized = sourceContents.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.IsFalse(
            normalized.Contains("IJobQueue<JobRequest>\n, IJobQueueInverted", StringComparison.Ordinal),
            $"Existing base-list append must not put the comma at the start of the new line.\nFile contents:\n{sourceContents}");
        StringAssert.Contains(
            normalized,
            "public class JobQueue : IJobQueue<JobRequest>,\n    IJobQueueInverted",
            $"Interface list should use comma-trailing formatting.\nFile contents:\n{sourceContents}");
    }

    [TestMethod]
    public async Task Extract_Interface_Preview_Generates_Formatted_Interface_File_Across_Projects()
    {
        // Regression for dr-9-2-format-bug-001-cross-project-interface-extractio.
        // Before the fix: the generated interface file read
        //     `publicinterfaceIAnimalService{...}`
        // with every token glued together, and the source class's base list emitted
        //     `public class AnimalService\n : IAnimalService{`
        // with `{` glued to the interface name. After the fix both shapes are readable C#.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var interfaceText = await File.ReadAllTextAsync(interfaceFilePath, CancellationToken.None);

        // --- Interface file whitespace assertions ---
        // (1) Keywords and identifiers must be separated by whitespace.
        Assert.IsFalse(
            interfaceText.Contains("publicinterface", StringComparison.Ordinal),
            $"Interface file has glued-together tokens (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");
        Assert.IsFalse(
            interfaceText.Contains("interfaceIAnimalService", StringComparison.Ordinal),
            $"Interface file has glued-together tokens (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");
        StringAssert.Contains(interfaceText, "public interface IAnimalService");

        // (2) Opening brace must be on a line following the declaration (either same-line with a
        //     preceding space or on its own line).
        Assert.IsFalse(
            interfaceText.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Interface file has opening brace glued to declaration identifier (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");

        // (3) File must span multiple lines (reformatted output, not a one-liner).
        var lineCount = interfaceText.Split('\n').Length;
        Assert.IsTrue(
            lineCount >= 4,
            $"Interface file was emitted on {lineCount} line(s); expected at least 4 for a formatted file.\nFile contents:\n{interfaceText}");

        // (4) Parameter lists and method signatures must have whitespace preserved.
        Assert.IsFalse(
            interfaceText.Contains("IEnumerable<IAnimal>animals", StringComparison.Ordinal),
            $"Interface file has glued-together parameter type and name (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");

        // --- Source file base-list assertions ---
        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);
        Assert.IsTrue(
            sourceContents.Contains("IAnimalService", StringComparison.Ordinal),
            "Source class should declare the new interface in its base list.");
        // The `{` of the class body must not be glued onto the interface name.
        Assert.IsFalse(
            sourceContents.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Source class has `{{` glued onto the interface base type (FORMAT-BUG-001 regression).\nFile contents:\n{sourceContents}");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Preview_Preserves_Source_File_Formatting()
    {
        // Regression for dr-9-3-format-bug-002-destroys-source-formatting (FORMAT-BUG-002).
        // Before the fix: CreateInterfaceExtractionSolutionAsync called
        //     updatedSourceRoot = ((CompilationUnitSyntax)updatedSourceRoot).NormalizeWhitespace();
        // which re-flowed the ENTIRE source compilation unit — collapsed intentional spacing,
        // dropped blank lines, reshuffled indentation. After the fix the source file's original
        // trivia is preserved; only the targeted `: IName` edit is applied.
        //
        // Seed the source file with distinctive whitespace (multiple spaces inside a parameter
        // list, blank lines between members, trailing whitespace) so NormalizeWhitespace would
        // be instantly observable as a regression.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var consumerFilePath = workspace.GetPath("SampleApp", "AnimalCoordinator.cs");
        File.WriteAllText(
            consumerFilePath,
            "using SampleLib;\n\npublic class AnimalCoordinator\n{\n    public AnimalCoordinator(AnimalService service)\n    {\n    }\n}\n");

        const string distinctiveSource = """
            using System.Threading;

            namespace SampleLib;

            public class AnimalService
            {


                public    void    MakeThemSpeak(   IEnumerable<IAnimal>     animals   )
                {
                    foreach (var animal in animals)
                    {
                        var sound = animal.Speak();
                        Console.WriteLine($"{animal.Name} says {sound}");
                    }
                }

                public int CountAnimals(List<IAnimal> animals)
                {
                    return animals.Count;
                }
            }

            """;
        File.WriteAllText(sourceFilePath, distinctiveSource);

        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);

        // (1) Distinctive parameter-list spacing survives the preview+apply round-trip.
        StringAssert.Contains(
            sourceContents,
            "MakeThemSpeak(   IEnumerable<IAnimal>     animals   )",
            $"Source parameter-list whitespace was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (2) Distinctive spacing inside the signature (between modifier, return type, and name)
        //     survives. NormalizeWhitespace would rewrite `public    void    MakeThemSpeak` as
        //     `public void MakeThemSpeak`.
        StringAssert.Contains(
            sourceContents,
            "public    void    MakeThemSpeak",
            $"Source signature whitespace was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (3) Multi-line blank-line separation between members survives. The original source was
        //     written with `\n` line endings; the AddBaseType edit may introduce a `\r\n` where
        //     the class body's `{` was relocated, but the two consecutive blank lines between `{`
        //     and the first member must survive intact. Normalize to `\n` before asserting.
        var normalized = sourceContents.Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(
            normalized,
            "\n{\n\n\n    public",
            $"Source blank-line structure was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (4) The targeted `: IAnimalService` edit was applied with correct spacing.
        StringAssert.Contains(sourceContents, "class AnimalService : IAnimalService");
        Assert.IsFalse(
            sourceContents.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Source class has `{{` glued onto the interface base type (regression).\nFile contents:\n{sourceContents}");

        // (5) Consumer parameter-type replacement preserved the space between type and name.
        var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
        StringAssert.Contains(
            consumerContents,
            "IAnimalService service",
            $"Consumer parameter lost whitespace between type and name (FORMAT-BUG-002 regression).\nFile contents:\n{consumerContents}");
        Assert.IsFalse(
            consumerContents.Contains("IAnimalServiceservice", StringComparison.Ordinal),
            $"Consumer parameter has glued-together type and name (FORMAT-BUG-002 regression).\nFile contents:\n{consumerContents}");

        // (6) gh #765 — the DI-inversion path routes through CreateInterfaceExtractionSolutionAsync
        // which calls CreateInterfaceCompilationUnit just like PreviewExtractInterfaceAsync. The
        // generated interface in Contracts references IAnimal (lives in SampleLib) via the
        // MakeThemSpeak / CountAnimals signatures; the using MUST be emitted or the apply round-trip
        // produces a CS0246 in the Contracts project. Guards against silent regression of the
        // semantic-walker pair on this path.
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        Assert.IsTrue(File.Exists(interfaceFilePath), "DI-inversion path must emit interface file.");
        var interfaceContents = await File.ReadAllTextAsync(interfaceFilePath, CancellationToken.None);
        StringAssert.Contains(
            interfaceContents,
            "using SampleLib;",
            $"DI-inversion interface MUST emit `using SampleLib;` — IAnimal is referenced by " +
            $"the synthesized interface signatures and lives in the source project's namespace (gh #765).\nFile contents:\n{interfaceContents}");
    }

    [TestMethod]
    public async Task Extract_Interface_Cross_Project_Emits_Using_For_Source_Project_Types()
    {
        // Regression for gh #765 (extract-interface-cross-project-uncompilable). Before the fix:
        // CrossProjectRefactoringService.CreateInterfaceCompilationUnit routed usings through
        // FilterUsingsForMember — a text-grep over the synthesized interface body that compared
        // each source-file using's last namespace segment against MinimallyQualifiedFormat short
        // names emitted into the interface text. When the source method's signature referenced a
        // type from a sibling namespace inside the SAME source project (e.g. `Shape` from
        // `SampleLib.Hierarchy`), the using's last segment (`Hierarchy`) never matched the
        // short name (`Shape`), so the using was dropped. The cross-project fallback at the
        // end of FilterUsingsForMember refused the full-source fallback, leaving an EMPTY
        // using list — producing a generated interface file whose method signature referenced
        // `Shape` without a `using SampleLib.Hierarchy;`, yielding CS0246 at compile time.
        //
        // After the fix: CollectReferencedNamespaces walks the type's public-instance member
        // symbols and collects every referenced type's containing namespace. The generated
        // interface emits a `using` for every collected namespace (excluding the interface's
        // own target namespace, which would be self-referential).
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");

        // Replace SampleLib/AnimalService.cs with a fixture whose method signature pulls in a
        // type from a sibling namespace (SampleLib.Hierarchy.Shape). The interface file must
        // emit `using SampleLib.Hierarchy;` or the apply round-trip produces uncompilable code.
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        const string sourceWithSiblingNamespaceDependency = """
            using SampleLib.Hierarchy;

            namespace SampleLib;

            public class AnimalService
            {
                public Shape GetShape()
                {
                    throw new System.NotImplementedException();
                }

                public List<Shape> GetShapes()
                {
                    return new List<Shape>();
                }
            }
            """;
        File.WriteAllText(sourceFilePath, sourceWithSiblingNamespaceDependency);

        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var interfaceText = await File.ReadAllTextAsync(interfaceFilePath, CancellationToken.None);

        // (1) The required using for the source-project sibling namespace MUST be emitted.
        // Without this the generated interface references `Shape` with no `using`, producing
        // CS0246 in the consumer project at compile time.
        StringAssert.Contains(
            interfaceText,
            "using SampleLib.Hierarchy;",
            $"Generated interface MUST emit `using SampleLib.Hierarchy;` — the source type's " +
            $"method signature references Shape from that namespace (gh #765).\nFile contents:\n{interfaceText}");

        // (2) The synthesized interface signature itself must reference the short name
        // `Shape` (MinimallyQualifiedFormat) — proving the using actually disambiguates it.
        StringAssert.Contains(interfaceText, "Shape GetShape");

        // (3) The generated file must parse as valid C# (sanity that the structural shape is intact).
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(interfaceText);
        var parseDiagnostics = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.AreEqual(
            0,
            parseDiagnostics.Count,
            $"Generated interface must parse as valid C#. Diagnostics: {string.Join("; ", parseDiagnostics.Select(d => d.ToString()))}\nFile contents:\n{interfaceText}");

        // (4) The interface's target namespace must NOT also appear as a self-referential using
        // (e.g. if the target namespace happens to match a referenced symbol's namespace by
        // coincidence, the walker is supposed to exclude it). The target namespace here is
        // derived from the Contracts project root, so it should be just "Contracts" — and there
        // should not be a `using Contracts;` line because no symbol lives in that namespace yet.
        Assert.IsFalse(
            interfaceText.Contains("using Contracts;", StringComparison.Ordinal),
            $"Generated interface must not emit a using for its own target namespace.\nFile contents:\n{interfaceText}");
    }

    [TestMethod]
    public async Task Extract_Interface_Cross_Project_Recurses_Into_Generic_Arguments_For_Source_Project_Types()
    {
        // Companion to Extract_Interface_Cross_Project_Emits_Using_For_Source_Project_Types.
        // Covers the recursive case: `Task<SourceProjectType>` must contribute both
        // `System.Threading.Tasks` AND the source-project namespace (the inner generic
        // argument). Mirrors `ExtractInterface_Recurses_Into_Generic_Arguments` in the
        // same-project semantic-using tests.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        const string sourceWithGenericArg = """
            using System.Threading.Tasks;
            using SampleLib.Hierarchy;

            namespace SampleLib;

            public class AnimalService
            {
                public Task<Shape> FetchAsync()
                {
                    throw new System.NotImplementedException();
                }
            }
            """;
        File.WriteAllText(sourceFilePath, sourceWithGenericArg);

        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var interfaceText = await File.ReadAllTextAsync(interfaceFilePath, CancellationToken.None);
        StringAssert.Contains(
            interfaceText,
            "using System.Threading.Tasks;",
            $"Generated interface MUST emit `using System.Threading.Tasks;` — the outer generic Task lives there.\nFile contents:\n{interfaceText}");
        StringAssert.Contains(
            interfaceText,
            "using SampleLib.Hierarchy;",
            $"Generated interface MUST emit `using SampleLib.Hierarchy;` — the inner generic " +
            $"argument Shape lives there. Recursion through TypeArguments is the gh #765 fix.\nFile contents:\n{interfaceText}");
    }

    [TestMethod]
    public async Task Move_Type_To_Project_Preview_Rejects_Circular_Dependency_With_Human_Readable_Names()
    {
        // Regression for move-type-to-project-preview-leaks-projectid-tokens.
        // Before the fix: EnsureProjectReference called solution.AddProjectReference with no
        // cycle pre-check, causing Roslyn internals to throw an InvalidOperationException whose
        // message contained raw "(ProjectId, #<guid> - <abs-path>)" tuple strings.
        // After the fix: the error message uses human-readable project names.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Set up CircularTarget with a pre-existing reference back to SampleLib.
        // CircularTarget -> SampleLib already exists, so adding SampleLib -> CircularTarget
        // would create a cycle.
        AddProjectToCopiedSolution(workspace.RootPath, "CircularTarget", "net10.0");
        var circularTargetCsproj = workspace.GetPath("CircularTarget", "CircularTarget.csproj");
        var sampleLibRelativePath = Path.Combine("..", "SampleLib", "SampleLib.csproj");
        File.WriteAllText(
            circularTargetCsproj,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"{sampleLibRelativePath}\" />\n  </ItemGroup>\n</Project>\n");

        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
                workspace.WorkspaceId,
                sourceFilePath,
                "Dog",
                "CircularTarget",
                null,
                CancellationToken.None,
                preserveNamespace: false));

        // Message must contain the human-readable project names.
        StringAssert.Contains(exception.Message, "SampleLib");
        StringAssert.Contains(exception.Message, "CircularTarget");

        // Message must NOT contain raw ProjectId tuple tokens leaked from Roslyn internals.
        Assert.IsFalse(
            exception.Message.Contains("ProjectId", StringComparison.Ordinal),
            $"Error message leaks raw ProjectId token (regression).\nActual message: {exception.Message}");
    }

    /// <summary>
    /// Perf-refactor regression (refactor-services-full-solution-scan-perf): dependency-inversion's
    /// post-extraction constructor rewrite now uses <c>SymbolFinder.FindReferencesAsync</c> instead
    /// of a brute-force scan over every document in the solution. This must still rewrite a
    /// constructor parameter in a project that references the extracted type only TRANSITIVELY
    /// (Downstream → MidLib → SampleLib), which the old full-solution walk covered by inspecting
    /// every project unconditionally.
    /// </summary>
    [TestMethod]
    public async Task Dependency_Inversion_Rewrites_Constructor_Parameter_In_Transitively_Referencing_Project()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        // MidLib references SampleLib directly; Downstream references MidLib only — so Downstream's
        // reference to SampleLib (and AnimalService) is purely transitive.
        AddProjectWithReferenceToCopiedSolution(workspace.RootPath, "MidLib", "net10.0", "SampleLib/SampleLib.csproj");
        AddProjectWithReferenceToCopiedSolution(workspace.RootPath, "Downstream", "net10.0", "MidLib/MidLib.csproj");

        var downstreamDirectory = Path.Combine(workspace.RootPath, "Downstream");
        var consumerFilePath = Path.Combine(downstreamDirectory, "DownstreamCoordinator.cs");
        File.WriteAllText(
            consumerFilePath,
            "using SampleLib;\n\nnamespace Downstream;\n\npublic class DownstreamCoordinator\n{\n    public DownstreamCoordinator(AnimalService service)\n    {\n    }\n}\n");

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        // Restore so MSBuild's transitive project-reference flow makes SampleLib (and
        // AnimalService) visible to Downstream through MidLib — without it the transitive hop is
        // inactive and AnimalService would not resolve in Downstream at all.
        await RestoreWorkspaceAsync(workspace, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
        Assert.IsFalse(
            consumerContents.Contains("(AnimalService service", StringComparison.Ordinal),
            $"Transitively-referencing consumer still declares the concrete type — FindReferencesAsync missed it.\nConsumer contents:\n{consumerContents}");
        StringAssert.Contains(consumerContents, "IAnimalService service");
    }

    /// <summary>
    /// Runs <c>dotnet restore</c> on the copied solution so newly-added projects acquire their
    /// <c>obj/project.assets.json</c> (and transitive project-reference graph) before the workspace
    /// is loaded. Mirrors the restore helper in <c>ScaffoldingFirstTestFileTests</c>.
    /// </summary>
    private static async Task RestoreWorkspaceAsync(IsolatedWorkspaceScope workspace, CancellationToken ct)
    {
        var execution = await DotnetCommandRunner.RunAsync(
            workingDirectory: workspace.RootPath,
            targetPath: workspace.SolutionPath,
            arguments: ["restore", workspace.SolutionPath, "--nologo"],
            ct).ConfigureAwait(false);

        Assert.IsTrue(
            execution.Succeeded,
            $"dotnet restore failed for test fixture. ExitCode={execution.ExitCode} StdOut={execution.StdOut} StdErr={execution.StdErr}");
    }

    private static void AddProjectWithReferenceToCopiedSolution(
        string copiedRoot,
        string projectName,
        string targetFramework,
        string referencedProjectRelativePath)
    {
        var projectDirectory = Path.Combine(copiedRoot, projectName);
        Directory.CreateDirectory(projectDirectory);

        var includePath = ".." + Path.DirectorySeparatorChar + referencedProjectRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var projectFilePath = Path.Combine(projectDirectory, projectName + ".csproj");
        File.WriteAllText(
            projectFilePath,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>{targetFramework}</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n  <ItemGroup>\n    <ProjectReference Include=\"{includePath}\" />\n  </ItemGroup>\n</Project>\n");

        var solutionFilePath = Path.Combine(copiedRoot, "SampleSolution.slnx");
        var solutionDocument = XDocument.Load(solutionFilePath, LoadOptions.PreserveWhitespace);
        solutionDocument.Root?.Add(new XElement("Project", new XAttribute("Path", $"{projectName}/{projectName}.csproj")));
        solutionDocument.Save(solutionFilePath, SaveOptions.DisableFormatting);
    }

    private static void AddProjectToCopiedSolution(string copiedRoot, string projectName, string targetFramework)
    {
        var projectDirectory = Path.Combine(copiedRoot, projectName);
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, projectName + ".csproj");
        File.WriteAllText(projectFilePath, $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>{targetFramework}</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");

        var solutionFilePath = Path.Combine(copiedRoot, "SampleSolution.slnx");
        var solutionDocument = XDocument.Load(solutionFilePath, LoadOptions.PreserveWhitespace);
        solutionDocument.Root?.Add(new XElement("Project", new XAttribute("Path", $"{projectName}/{projectName}.csproj")));
        solutionDocument.Save(solutionFilePath, SaveOptions.DisableFormatting);
    }
}
