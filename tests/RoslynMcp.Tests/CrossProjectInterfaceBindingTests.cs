using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectInterfaceBindingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow(false, "Contracts", "ICounter", "alias")]
    [DataRow(true, "Contracts", "ICounter", "alias")]
    [DataRow(false, "SampleLib", "ICounter", "alias")]
    [DataRow(true, "", "ICounter", "alias")]
    [DataRow(false, "@event", "@interface", "alias")]
    [DataRow(false, "SampleLib", "ICounter", "simple")]
    [DataRow(false, "SampleLib", "ICounter", "namespace-alias")]
    [DataRow(false, "SampleLib.Collision", "IAdded", "simple")]
    public async Task Preview_BindsGeneratedInterfaceWithoutChangingUnrelatedNames(
        bool invert, string targetNamespace, string interfaceName, string referenceShape)
    {
        var scope = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            AddProjectToCopiedSolution(scope.RootPath, "Contracts", "net10.0");
            var projectPath = scope.GetPath("Contracts", "Contracts.csproj");
            var xml = XDocument.Load(projectPath);
            xml.Root!.Element("PropertyGroup")!.Add(new XElement("RootNamespace", targetNamespace));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            var sourcePath = scope.GetPath("SampleLib", "Counter.cs");
            var originalSource = """
                namespace SampleLib
                {
                    using ICounter = Collision.ICounter;
                    public class Counter
                    {
                        public int Count() => 1;
                        public System.Collections.Generic.List<int> Values() => [];
                    }
                    public class Unrelated
                    {
                        public ICounter Existing => null!;
                        public System.Collections.Generic.List<ICounter> Constructed => null!;
                        public const string AliasName = nameof(/* before alias */ ICounter /* after alias */);
                    }
                }
                """;
            originalSource = referenceShape switch
            {
                "alias" => originalSource.Replace("using ICounter = Collision.ICounter;", "using ICounter = Collision.OriginalContract;", StringComparison.Ordinal),
                "simple" => originalSource.Replace("using ICounter = Collision.ICounter;", "using Collision;", StringComparison.Ordinal),
                "namespace-alias" => originalSource.Replace("using ICounter = Collision.ICounter;", "using ICounter = Collision;", StringComparison.Ordinal)
                    .Replace("public ICounter Existing", "public ICounter::OriginalContract Existing", StringComparison.Ordinal)
                    .Replace("List<ICounter>", "List<ICounter::OriginalContract>", StringComparison.Ordinal),
                _ => throw new AssertFailedException("Unknown reference shape.")
            };
            await File.WriteAllTextAsync(sourcePath, originalSource);
            await File.WriteAllTextAsync(scope.GetPath("SampleLib", "Collision.cs"),
                "namespace Collision; public interface ICounter { } public interface OriginalContract { }");
            var expectedOriginalType = referenceShape == "simple" ? "Collision.ICounter" : "Collision.OriginalContract";
            var sourceConsumers = new Dictionary<string, string>();
            if (invert)
            {
                foreach (var name in new[] { "SourceConsumerOne", "SourceConsumerTwo" })
                {
                    var path = scope.GetPath("SampleLib", name + ".cs");
                    var text = "namespace SampleLib { using ICounter = global::Collision.OriginalContract; public class " + name +
                        " { public " + name + "(Counter value) { } public ICounter Unrelated => null!; } }";
                    sourceConsumers.Add(path, text);
                    await File.WriteAllTextAsync(path, text);
                }
            }
            if (invert)
                await File.WriteAllTextAsync(scope.GetPath("SampleApp", "AdditionalCounterConsumer.cs"),
                    "namespace Consumers; public class AdditionalCounterConsumer { public AdditionalCounterConsumer(SampleLib.Counter value) { } public global::Collision.OriginalContract Unrelated => null!; }");
            var consumerPath = scope.GetPath("SampleApp", "CounterConsumer.cs");
            await File.WriteAllTextAsync(consumerPath,
                "namespace Consumers; public class CounterConsumer { public CounterConsumer(SampleLib.Counter /* nullable trivia */ ? /* after nullable */ value) { } }");
            await RestoreCopiedSolutionAsync(scope.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await scope.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var before = await original.Projects.Single(item => item.Name == "SampleLib").GetCompilationAsync();
            var originalProperty = before!.GetTypeByMetadataName("SampleLib.Unrelated")!.GetMembers("Existing").OfType<IPropertySymbol>().Single();
            Assert.AreEqual(expectedOriginalType, originalProperty.Type.ToDisplayString());
            Assert.IsFalse(before.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", before.GetDiagnostics()));

            var preview = invert
                ? await CrossProjectRefactoringService.PreviewDependencyInversionAsync(scope.WorkspaceId,
                    sourcePath, "Counter", interfaceName, "Contracts", CancellationToken.None)
                : await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(scope.WorkspaceId,
                    sourcePath, "Counter", interfaceName, "Contracts", CancellationToken.None);
            var entry = PreviewStore.Retrieve(preview.PreviewToken) ?? throw new AssertFailedException("The preview must be retained.");
            var modified = entry.ModifiedSolution;
            var library = modified.Projects.Single(item => item.Name == "SampleLib");
            var compilation = await library.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            var concrete = compilation.GetTypeByMetadataName("SampleLib.Counter")!;
            var contract = concrete.Interfaces.Single();
            Assert.AreEqual("Contracts", contract.ContainingAssembly.Name);
            Assert.AreEqual(interfaceName.TrimStart('@'), contract.Name);
            var targetProject = modified.Projects.Single(item => item.Name == "Contracts");
            var targetDocument = targetProject.Documents.Single(item => item.Name == interfaceName + ".cs");
            var declaration = (await targetDocument.GetSyntaxRootAsync())!.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();
            var targetModel = await targetDocument.GetSemanticModelAsync();
            var declared = targetModel!.GetDeclaredSymbol(declaration)!;
            var namespaceSegments = new Stack<string>();
            for (var currentNamespace = declared.ContainingNamespace; !currentNamespace.IsGlobalNamespace; currentNamespace = currentNamespace.ContainingNamespace)
                namespaceSegments.Push(currentNamespace.Name);
            Assert.AreEqual(string.Join(".", targetNamespace.Split('.').Select(segment => segment.TrimStart('@'))), string.Join(".", namespaceSegments));
            Assert.AreEqual(declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var property = compilation.GetTypeByMetadataName("SampleLib.Unrelated")!.GetMembers("Existing").OfType<IPropertySymbol>().Single();
            Assert.AreEqual(expectedOriginalType, property.Type.ToDisplayString(), "Qualification must not rebind unrelated names.");
            var unrelated = compilation.GetTypeByMetadataName("SampleLib.Unrelated")!;
            var constructed = (INamedTypeSymbol)unrelated.GetMembers("Constructed").OfType<IPropertySymbol>().Single().Type;
            Assert.AreEqual(expectedOriginalType, constructed.TypeArguments.Single().ToDisplayString());
            Assert.AreEqual("ICounter", unrelated.GetMembers("AliasName").OfType<IFieldSymbol>().Single().ConstantValue);
            var preservedSource = await modified.Projects.SelectMany(item => item.Documents).Single(item => item.FilePath == sourcePath).GetTextAsync();
            StringAssert.Contains(preservedSource.ToString(), "/* before alias */");
            StringAssert.Contains(preservedSource.ToString(), "/* after alias */");
            Assert.AreEqual(originalSource, await File.ReadAllTextAsync(sourcePath));
            Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error),
                string.Join("; ", compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error)));
            if (invert)
            {
                foreach (var originalConsumer in sourceConsumers)
                {
                    var sourceConsumer = compilation.GetTypeByMetadataName("SampleLib." + Path.GetFileNameWithoutExtension(originalConsumer.Key))!;
                    var sourceParameter = sourceConsumer.InstanceConstructors.Single(item => item.Parameters.Length == 1).Parameters.Single();
                    Assert.IsTrue(SymbolEqualityComparer.Default.Equals(contract, sourceParameter.Type),
                        "Every source-project constructor must use the extracted contract, regardless of document processing order.");
                    var untouched = sourceConsumer.GetMembers("Unrelated").OfType<IPropertySymbol>().Single();
                    Assert.AreEqual("Collision.OriginalContract", untouched.Type.ToDisplayString());
                    Assert.AreEqual("SampleLib", untouched.Type.ContainingAssembly.Name);
                    Assert.AreEqual(originalConsumer.Value, await File.ReadAllTextAsync(originalConsumer.Key));
                }
                var app = await modified.Projects.Single(item => item.Name == "SampleApp").GetCompilationAsync();
                Assert.IsNotNull(app);
                var additional = app.GetTypeByMetadataName("Consumers.AdditionalCounterConsumer")!;
                var additionalParameter = additional.InstanceConstructors.Single(item => item.Parameters.Length == 1).Parameters.Single();
                Assert.AreEqual(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), additionalParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                Assert.AreEqual("Contracts", additionalParameter.Type.ContainingAssembly.Name);
                Assert.AreEqual("Collision.OriginalContract", additional.GetMembers("Unrelated").OfType<IPropertySymbol>().Single().Type.ToDisplayString());
                var consumer = app!.GetTypeByMetadataName("Consumers.CounterConsumer")!;
                var parameter = consumer.InstanceConstructors.Single(item => item.Parameters.Length == 1).Parameters.Single();
                Assert.AreEqual(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                Assert.AreEqual("Contracts", parameter.Type.ContainingAssembly.Name);
                Assert.AreEqual(NullableAnnotation.Annotated, parameter.NullableAnnotation);
                var consumerText = await modified.Projects.Single(item => item.Name == "SampleApp").Documents
                    .Single(document => document.FilePath == consumerPath).GetTextAsync();
                StringAssert.Contains(consumerText.ToString(), "/* nullable trivia */ ? /* after nullable */ value");
                Assert.IsFalse(app.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Interface binding fixture cleanup failed.", primary,
                () => scope.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractAndWire_SameFileRetainsBaseAndCompilesRegistration(bool caseDistinct)
    {
        if (caseDistinct && OperatingSystem.IsWindows())
            Assert.Inconclusive("This control requires case-distinct Unix file identity.");
        var scope = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            AddProjectToCopiedSolution(scope.RootPath, "Contracts", "net10.0");
            var sourcePath = scope.GetPath("SampleLib", "RegisteredCounter.cs");
            await File.WriteAllTextAsync(sourcePath, """
                namespace SampleLib
                {
                    using Microsoft.Extensions.DependencyInjection;
                    public class Counter { public int Count() => 1; }
                    public static class Registrations
                    {
                        public static void Register(IServiceCollection services) => services.AddSingleton<Counter>();
                    }
                }
                """);
            await File.WriteAllTextAsync(scope.GetPath("SampleLib", "RegistrationFixture.cs"), """
                namespace Microsoft.Extensions.DependencyInjection;
                public interface IServiceCollection { }
                public static class RegistrationFixture
                {
                    public static IServiceCollection AddSingleton<T>(this IServiceCollection services) => services;
                    public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TImplementation : TService => services;
                }
                """);
            string? otherPath = null;
            if (caseDistinct)
            {
                // SDK implicit-item duplicate checking folds case even on Unix. Use the
                // supported explicit Compile model so both physical source identities reach Roslyn.
                var libraryPath = scope.GetPath("SampleLib", "SampleLib.csproj");
                var libraryXml = XDocument.Load(libraryPath);
                libraryXml.Root!.Element("PropertyGroup")!.Add(new XElement("EnableDefaultCompileItems", "false"));
                libraryXml.Root.Add(new XElement("ItemGroup", new XElement("Compile",
                    new XAttribute("Include", "**/*.cs"),
                    new XAttribute("Exclude", "$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"))));
                await File.WriteAllTextAsync(libraryPath, libraryXml.ToString());
                otherPath = scope.GetPath("SampleLib", "registeredcounter.cs");
                await File.WriteAllTextAsync(otherPath, """
                    namespace SampleLib
                    {
                        using Microsoft.Extensions.DependencyInjection;
                        public static class OtherRegistrations
                        {
                            public static void Register(IServiceCollection services) => services.AddSingleton<Counter>();
                        }
                    }
                    """);
            }
            await RestoreCopiedSolutionAsync(scope.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await scope.LoadAsync();
            var before = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            Assert.AreEqual(1, before.Projects.SelectMany(project => project.Documents)
                .Count(document => string.Equals(document.FilePath, sourcePath, StringComparison.Ordinal)),
                "The actual native snapshot must retain the uppercase source identity.");
            if (otherPath is not null)
                Assert.AreEqual(1, before.Projects.SelectMany(project => project.Documents)
                    .Count(document => string.Equals(document.FilePath, otherPath, StringComparison.Ordinal)),
                    "The actual native snapshot must independently retain the lowercase source identity.");
            var store = new CompositePreviewStore();
            var orchestrator = new RoslynMcp.Roslyn.Services.ExtractAndWireOrchestrator(
                WorkspaceManager, store, PreviewStore, CrossProjectRefactoringService, DiRegistrationService);
            var preview = await orchestrator.PreviewExtractAndWireInterfaceAsync(scope.WorkspaceId,
                sourcePath, "Counter", "ICounter", "Contracts", true, CancellationToken.None);
            var entry = store.Retrieve(preview.PreviewToken)!.Value;
            var edited = entry.Mutations.Single(item => item.FilePath == sourcePath).UpdatedContent!;
            StringAssert.Contains(edited, "Counter : global::Contracts.ICounter");
            StringAssert.Contains(edited, "AddSingleton<global::Contracts.ICounter, Counter>");
            var original = await File.ReadAllTextAsync(sourcePath);
            Assert.IsFalse(original.Contains("global::Contracts.ICounter", StringComparison.Ordinal), "Preview must retain original disk bytes.");
            var reviewed = preview.Changes.Single(item => item.FilePath == sourcePath).UnifiedDiff;
            StringAssert.Contains(reviewed, "global::Contracts.ICounter");
            var library = before.Projects.Single(item => item.Name == "SampleLib");
            var contracts = before.Projects.Single(item => item.Name == "Contracts");
            var projectMutation = entry.Mutations.Single(item => item.FilePath == library.FilePath);
            Assert.IsTrue(XDocument.Parse(projectMutation.UpdatedContent!).Descendants("ProjectReference")
                .Any(item => ((string?)item.Attribute("Include"))?.Contains("Contracts.csproj", StringComparison.Ordinal) == true));
            var interfacePath = scope.GetPath("Contracts", "ICounter.cs");
            var interfaceMutation = entry.Mutations.Single(item => item.FilePath == interfacePath);
            var sourceDocument = library.Documents.Single(item => item.FilePath == sourcePath);
            var compiled = before.WithDocumentText(sourceDocument.Id, SourceText.From(edited))
                .AddDocument(DocumentId.CreateNewId(contracts.Id), "ICounter.cs", SourceText.From(interfaceMutation.UpdatedContent!), filePath: interfacePath)
                .AddProjectReference(library.Id, new ProjectReference(contracts.Id));
            if (otherPath is not null)
            {
                var otherMutation = entry.Mutations.Single(item => item.FilePath == otherPath);
                StringAssert.Contains(otherMutation.UpdatedContent!, "OtherRegistrations");
                StringAssert.Contains(otherMutation.UpdatedContent!, "AddSingleton<global::Contracts.ICounter, Counter>");
                Assert.IsFalse(otherMutation.UpdatedContent!.Contains("class Counter", StringComparison.Ordinal),
                    "Case-distinct registration file must not receive the extracted class file's content.");
                Assert.IsTrue(preview.Changes.Any(item => item.FilePath == otherPath));
                var otherDocument = library.Documents.Single(item => item.FilePath == otherPath);
                compiled = compiled.WithDocumentText(otherDocument.Id, SourceText.From(otherMutation.UpdatedContent!));
            }
            var compilation = await compiled.GetProject(library.Id)!.GetCompilationAsync();
            var errors = compilation!.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join("; ", errors.Select(item => item.ToString())));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Same-file wiring fixture cleanup failed.", primary,
                () => scope.DisposeAsync());
        }
    }
}
