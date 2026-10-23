using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeConditionProtocolTests
{
    [TestMethod]
    public async Task WildcardImports_PreserveNativeResolutionOrderAcrossImplicitSdkImports()
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            var imports = Path.Combine(fixture.Source, "imports");
            Directory.CreateDirectory(imports);
            var first = Path.Combine(imports, "a.props");
            var second = Path.Combine(imports, "b.props");
            await File.WriteAllTextAsync(first, "<Project><PropertyGroup><OutputPath>$(Root)/first/</OutputPath></PropertyGroup></Project>");
            await File.WriteAllTextAsync(second, "<Project><PropertyGroup><OutputPath>$(Root)/second/</OutputPath></PropertyGroup></Project>");
            var project = Path.Combine(fixture.Source, "ImportOrder.csproj");
            await File.WriteAllTextAsync(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"imports/*.props\" /></Project>");
            var node = EvaluatedBuildGraph.FromRoots([project], new Dictionary<string, string> { ["Root"] = fixture.Source },
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            var declarations = node.PathEvidence.Declarations.Where(declaration => declaration.Name == "OutputPath"
                && (FileSystemPath.Comparer.Equals(declaration.FilePath, first) || FileSystemPath.Comparer.Equals(declaration.FilePath, second))).ToArray();
            CollectionAssert.AreEqual(new[] { first, second }, declarations.Select(declaration => declaration.FilePath).ToArray());
            Assert.IsTrue(node.PathEvidence.Directories.Any(path => FileSystemPath.Comparer.Equals(
                Path.TrimEndingDirectorySeparator(path), Path.Combine(fixture.Source, "second", "net10.0"))));
            Assert.IsTrue(node.BuildInputs.Definitions.Any(path => Path.GetFileName(path) == "Sdk.props"));
            Assert.IsTrue(node.BuildInputs.Definitions.Any(path => Path.GetFileName(path) == "Sdk.targets"));
        });
    }

    [TestMethod]
    [DataRow("paren(.flag", false)]
    [DataRow("paren(.flag", true)]
    [DataRow("paren).flag", false)]
    [DataRow("paren).flag", true)]
    [DataRow("paren().flag", false)]
    [DataRow("paren().flag", true)]
    public async Task QuotedNativeFunctionArgument_ParenthesesRemainFilenameData(string marker, bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, marker), "source marker");
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "unrelated.txt"), "semantic input");
            if (privateMarker) await File.WriteAllTextAsync(Path.Combine(fixture.Private, marker), "private marker");
            var condition = "Exists('$([System.IO.Path]::Combine($(Root), `" + marker + "`))')";
            var definition = new XElement("Project", new XElement("PropertyGroup",
                new XElement("SemanticRead", "$([System.IO.File]::ReadAllText('$(Root)/unrelated.txt'))"),
                new XElement("Selected", new XAttribute("Condition", condition), "true")));
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            var path = Path.Combine(fixture.Source, "quoted-function.proj");
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            Assert.AreEqual("true", MsBuildMetadataHelper.EvaluateProperties(path, globals, "Selected")[0],
                "Native MSBuild must establish that parentheses inside its quoted argument are filename data.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None).Nodes.Single().PathEvidence;
            Assert.AreEqual(1, evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, marker))));
            Assert.IsFalse(evidence.ConditionProbes.Any(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, "unrelated.txt"))));
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "quoted-function.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            Assert.AreEqual(privateMarker, MsBuildMetadataHelper.EvaluateProperties(privatePath, globals, "Selected")[0] == "true");
            Assert.AreEqual(fixture.Source, globals["Root"]);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task IdenticalPathPredicates_RetainExactExecutedOccurrence(bool skippedFirst)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "marker.flag"), "source");
            const string call = "Exists('$(Root)/marker.flag')";
            var condition = skippedFirst ? "('true' == 'true' Or " + call + ") And " + call
                : call + " And ('true' == 'true' Or " + call + ")";
            var definition = new XElement("Project", new XElement("PropertyGroup",
                new XElement("Selected", new XAttribute("Condition", condition), "true")));
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            var path = Path.Combine(fixture.Source, "identical-path.proj");
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            Assert.AreEqual("true", MsBuildMetadataHelper.EvaluateProperties(path, globals, "Selected")[0],
                "The original native expression must select the source marker before capture.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None).Nodes.Single().PathEvidence;
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "identical-path.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            Assert.AreEqual("", MsBuildMetadataHelper.EvaluateProperties(privatePath, globals, "Selected")[0],
                "Only the executed occurrence may determine private selection; the original source marker must not satisfy it.");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreEqual("source", await File.ReadAllTextAsync(Path.Combine(fixture.Source, "marker.flag")));
        });
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task UnquotedNativeExistsOperand_RetainsPrivateSelection(bool sourceMarker, bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            if (sourceMarker) await File.WriteAllTextAsync(Path.Combine(fixture.Source, "marker.flag"), "source");
            if (privateMarker) await File.WriteAllTextAsync(Path.Combine(fixture.Private, "marker.flag"), "private");
            var definition = new XElement("Project", new XElement("PropertyGroup",
                new XElement("Selected", new XAttribute("Condition", "Exists($(MarkerPath))"), "true")));
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            var sourcePath = Path.Combine(fixture.Source, "unquoted.proj");
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var globals = new Dictionary<string, string> { ["MarkerPath"] = Path.Combine(fixture.Source, "marker.flag") };
            Assert.AreEqual(sourceMarker, MsBuildMetadataHelper.EvaluateProperties(sourcePath, globals, "Selected")[0] == "true",
                "Native MSBuild must establish the unquoted-argument premise before observer capture.");
            var evidence = EvaluatedBuildGraph.FromRoots([sourcePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single().PathEvidence;
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(sourcePath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "unquoted.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            Assert.AreEqual(privateMarker, MsBuildMetadataHelper.EvaluateProperties(privatePath, globals, "Selected")[0] == "true");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(sourcePath));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CachedImportFunction_UsesObservedNativeArgumentForPrivateSelection(bool explicitProperty)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Source, "imports"));
            var firstPath = Path.Combine(fixture.Source, "imports", "inner.props");
            await File.WriteAllTextAsync(firstPath, "<Project><PropertyGroup><First>true</First></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "imports", "other.props"),
                "<Project><PropertyGroup><Second>true</Second></PropertyGroup></Project>");
            const string function = "$([System.IO.Path]::Combine($(Root), imports/inner.props))";
            var definition = new XElement("Project", new XElement("Import", new XAttribute("Project", "imports/inner.props"),
                new XAttribute("Condition", "'$(LoadFirst)' == 'true'")));
            if (explicitProperty) definition.Add(new XElement("PropertyGroup", new XElement("CachedPath", function)));
            definition.Add(new XElement("Import", new XAttribute("Project", "imports/other.props"),
                new XAttribute("Condition", "Exists('" + (explicitProperty ? "$(CachedPath)" : function) + "')")));
            var path = Path.Combine(fixture.Source, "cached-function.proj");
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source, ["LoadFirst"] = "true" };
            CollectionAssert.AreEqual(new[] { "true", "true" }, MsBuildMetadataHelper.EvaluateProperties(path, globals, "First", "Second"),
                "The native direct function and its explicit-property form must both select the second import.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single().PathEvidence;
            Assert.AreEqual(1, evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(firstPath, probe.Path)),
                "The supported native evaluation must supply the actual function argument, not a guessed cache target.");
            Directory.CreateDirectory(Path.Combine(fixture.Private, "imports"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Private, "imports", "other.props"),
                "<Project><PropertyGroup><Second>true</Second></PropertyGroup></Project>");
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "cached-function.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            var privateGlobals = new Dictionary<string, string>(globals) { ["LoadFirst"] = "false" };
            Assert.AreEqual("", MsBuildMetadataHelper.EvaluateProperties(privatePath, privateGlobals, "Second")[0],
                "A missing private predicate input must not be satisfied by the original source import.");
            Assert.AreEqual(fixture.Source, privateGlobals["Root"]);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImportPredicate_UsesCacheStateAtActualNativeExecution(bool importFirst)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Source, "imports"));
            var importedPath = Path.Combine(fixture.Source, "imports", "inner.props");
            await File.WriteAllTextAsync(importedPath, "<Project><PropertyGroup><Imported>true</Imported></PropertyGroup></Project>");
            var import = new XElement("Import", new XAttribute("Project", "imports/inner.props"));
            var predicate = new XElement("PropertyGroup", new XAttribute("Condition", "Exists('$(Root)/imports/inner.props')"),
                new XElement("Observed", "true"));
            var definition = new XElement("Project", importFirst ? new[] { import, predicate } : new[] { predicate, import });
            var path = Path.Combine(fixture.Source, "cache-order.proj");
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            CollectionAssert.AreEqual(new[] { "true", "true" }, MsBuildMetadataHelper.EvaluateProperties(path, globals, "Observed", "Imported"),
                "Both native import orders must establish the same predicate truth before observer capture.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single().PathEvidence;
            Assert.AreEqual(1, evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(importedPath, probe.Path)));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    public async Task CachedImportPredicate_BindsActualLoadedCoordinateAcrossNativeCaseComparison()
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Source, "imports"));
            var imported = Path.Combine(fixture.Source, "imports", "inner.props");
            await File.WriteAllTextAsync(imported, "<Project />");
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "imports", "selected.props"),
                "<Project><PropertyGroup><Selected>true</Selected></PropertyGroup></Project>");
            var definition = new XElement("Project",
                new XElement("Import", new XAttribute("Project", "imports/inner.props")),
                new XElement("Import", new XAttribute("Project", "imports/selected.props"),
                    // Cache keys preserve separator spelling. Vary only case so this
                    // control actually queries the native cache on both platforms.
                    new XAttribute("Condition", "Exists('$(Root)" + Path.DirectorySeparatorChar
                        + "imports" + Path.DirectorySeparatorChar + "INNER.props')")));
            var path = Path.Combine(fixture.Source, "cache-case.proj");
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            Assert.AreEqual("true", MsBuildMetadataHelper.EvaluateProperties(path, globals, "Selected")[0],
                "The installed native cache must independently establish case-insensitive lookup.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None).Nodes.Single().PathEvidence;
            var probe = evidence.ConditionProbes.Single();
            Assert.AreEqual(imported, probe.Path);
            Assert.AreEqual(imported, probe.QueryPath);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    [DataRow("'true' == 'true' Or", true, false)]
    [DataRow("'false' == 'true' And", false, false)]
    [DataRow("'true' == 'true' And", true, true)]
    public async Task CachedProjectPredicate_DistinguishesNativeExecutionFromShortCircuit(string prefix, bool selected, bool executed)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            var path = Path.Combine(fixture.Source, "cached-predicate.proj");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("PropertyGroup",
                new XElement("Selected", new XAttribute("Condition", prefix + " Exists('$(MSBuildProjectFullPath)')"), "true"))).ToString());
            await File.WriteAllBytesAsync(path, bytes);
            var globals = new Dictionary<string, string>();
            Assert.AreEqual(selected, MsBuildMetadataHelper.EvaluateProperties(path, globals, "Selected")[0] == "true",
                "Establish native cached-file predicate and boolean short-circuit semantics first.");
            var evidence = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single().PathEvidence;
            Assert.AreEqual(executed ? 1 : 0, evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(path, probe.Path)),
                "A cached file cannot turn an unexecuted Exists operand into authorized input evidence.");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CachedProjectFile_WithDirectorySuffix_RetainsNativeFalseResult(bool nativeFunction)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            var predicate = nativeFunction ? "Exists('$([System.String]::Concat($(MSBuildProjectFullPath), /))')"
                : "Exists('$(MSBuildProjectFullPath)/')";
            var sourcePath = Path.Combine(fixture.Source, "cached-file.proj");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("PropertyGroup",
                new XElement("Selected", new XAttribute("Condition", predicate), "true"))).ToString());
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var globals = new Dictionary<string, string>();
            Assert.AreEqual("", MsBuildMetadataHelper.EvaluateProperties(sourcePath, globals, "Selected")[0],
                "A cached native project is still a file when a directory suffix is requested.");
            var evidence = EvaluatedBuildGraph.FromRoots([sourcePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single().PathEvidence;
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(sourcePath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "cached-file.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            Assert.AreEqual("", MsBuildMetadataHelper.EvaluateProperties(privatePath, globals, "Selected")[0]);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(sourcePath));
        });
    }

    [TestMethod]
    [DataRow("file", "file", false)]
    [DataRow("file", "directory", false)]
    [DataRow("directory", "file", false)]
    [DataRow("directory", "directory", false)]
    [DataRow("missing", "directory", false)]
    [DataRow("directory", "missing", false)]
    [DataRow("file", "file", true)]
    [DataRow("directory", "file", true)]
    public async Task DirectorySuffixPredicate_PreservesNativeFileVersusDirectorySemantics(string sourceKind, string privateKind, bool nativeFunction)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            static void CreateMarker(string root, string kind)
            {
                var marker = Path.Combine(root, "marker");
                switch (kind)
                {
                    case "file": File.WriteAllText(marker, "a file is not a directory"); break;
                    case "directory": Directory.CreateDirectory(marker); break;
                    case "missing": break;
                    default: throw new AssertFailedException("Unknown owned marker fixture kind.");
                }
            }
            CreateMarker(fixture.Source, sourceKind);
            CreateMarker(fixture.Private, privateKind);
            var argument = nativeFunction ? "$([System.IO.Path]::Combine($(Root), marker/))" : "$(Root)/marker/";
            var predicate = "Exists('" + argument + "')";
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("PropertyGroup",
                new XElement("FunctionPath", argument),
                new XElement("ReferenceSelected", new XAttribute("Condition", "Exists('$(FunctionPath)')"), "true"),
                new XElement("Selected", new XAttribute("Condition", predicate), "true"))).ToString());
            var sourcePath = Path.Combine(fixture.Source, "directory-intent.proj");
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            // Each oracle and its operand observations come from the same native evaluation.
            // Fresh-file Win32 queries can initially report existence despite a trailing separator;
            // such a result must fail physical admission, not be retried or silently normalized.
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            var sourceObservation = ObserveDirectoryQuery(sourcePath, globals);
            var sourceAccepted = await AssertDirectoryQueryAdmissionAsync(sourceObservation, fixture.Source);
            string Map(string value) => FileSystemPath.Comparer.Equals(value, fixture.Source) || FileSystemPath.IsStrictDescendant(fixture.Source, value)
                ? Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, value)) : value;
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(sourcePath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, sourceObservation.Evidence.Declarations);
            var privatePath = Path.Combine(fixture.Private, "directory-intent.proj");
            await File.WriteAllBytesAsync(privatePath, rendered);
            var referencePath = Path.Combine(fixture.Private, "native-reference.proj");
            await File.WriteAllBytesAsync(referencePath, bytes);
            var referenceGlobals = new Dictionary<string, string>(globals) { ["Root"] = fixture.Private };
            var referenceObservation = ObserveDirectoryQuery(referencePath, referenceGlobals);
            var privateObservation = ObserveDirectoryQuery(privatePath, globals);
            // Always exercise both original private semantics and relocated replay, including
            // cases where the source's cold native observation was refused.
            var referenceAccepted = await AssertDirectoryQueryAdmissionAsync(referenceObservation, fixture.Private);
            var privateAccepted = await AssertDirectoryQueryAdmissionAsync(privateObservation, fixture.Private);
            CollectionAssert.AreEqual(referenceObservation.QueryPaths,
                privateObservation.QueryPaths,
                "Relocation must preserve each exact private native operand, including directory suffixes.");
            if (sourceKind != "file") Assert.IsTrue(sourceAccepted, "Stable directory/missing source evidence must be admitted.");
            if (privateKind != "file")
            {
                Assert.IsTrue(referenceAccepted, "Stable private native evidence must be admitted.");
                Assert.IsTrue(privateAccepted, "Stable private replay evidence must be admitted.");
            }
            if (referenceAccepted && privateAccepted)
                CollectionAssert.AreEqual(referenceObservation.Selections, privateObservation.Selections,
                    "Admitted private replay must match admitted original native semantics.");
            if (sourceAccepted)
                Assert.IsTrue(sourceObservation.Selections.All(selected => selected == (sourceKind == "directory")));
            if (referenceAccepted)
                Assert.IsTrue(referenceObservation.Selections.All(selected => selected == (privateKind == "directory")));
            if (privateAccepted)
                Assert.IsTrue(privateObservation.Selections.All(selected => selected == (privateKind == "directory")));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(sourcePath));
        });
    }

    [TestMethod]
    public void InMemoryCanary_ExercisesNativeApiWithoutAuthoredInputs()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        VerifyNativeCanary();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void VerifyNativeCanary()
    {
        using var observer = new NativeConditionEvidence();
        var owner = observer.Collection;
        owner.RegisterLogger(observer);
        // This deliberately calls the native API directly: any argument/ABI failure
        // has only synthetic canary inputs and is visible before graph aggregation.
        observer.VerifyProtocol(owner);
    }

    [TestMethod]
    [DataRow("'false' == 'true' And", false)]
    [DataRow("'true' == 'true' Or", true)]
    public async Task ShortCircuitedFunction_DoesNotEnrollOrRewriteUnexecutedOperand(string prefix, bool included)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var expression = prefix + " Exists('$([System.IO.Path]::Combine($(Outside), marker.flag))')";
            var result = await fixture.ReplayAsync(expression, [], [], unrelatedIo: false);
            Assert.AreEqual(included, result.SourceSelected);
            Assert.AreEqual(included, result.Selected);
            Assert.IsFalse(result.Evidence.ConditionProbes.Any(probe => FileSystemPath.Comparer.Equals(probe.Path, fixture.OutsideMarker)));
            Assert.AreEqual(expression, result.ReplayedCondition);
            Assert.AreEqual(fixture.Outside, result.NativeGlobals["Outside"]);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MultipleNativeFunctions_PreserveOperandOrderAndPrivateSelection(bool privateSecond)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var expression = "Exists('$([System.IO.Path]::Combine($(Root), first.flag))') And Exists('$([System.IO.Path]::Combine($(Root), second.flag))')";
            var result = await fixture.ReplayAsync(expression, ["first.flag", "second.flag"],
                privateSecond ? ["first.flag", "second.flag"] : ["first.flag"], unrelatedIo: true);
            Assert.IsTrue(result.SourceSelected, "Both native source predicates must be evaluated before private replay.");
            Assert.AreEqual(privateSecond, result.Selected);
            foreach (var name in new[] { "first.flag", "second.flag" })
                Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, name))));
            Assert.IsFalse(result.Evidence.ConditionProbes.Any(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, "unrelated.txt"))));
            Assert.AreEqual(fixture.Source, result.NativeGlobals["Root"]);
        });
    }

    [TestMethod]
    public async Task SecondShortCircuitedFunction_CannotBecomeOwnedThroughAnEarlierProbe()
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var expression = "Exists('$([System.IO.Path]::Combine($(Root), first.flag))') And Exists('$([System.IO.Path]::Combine($(Outside), marker.flag))')";
            var result = await fixture.ReplayAsync(expression, [], [], unrelatedIo: true);
            Assert.IsFalse(result.SourceSelected);
            Assert.IsFalse(result.Selected);
            Assert.IsFalse(result.Evidence.ConditionProbes.Any(probe => FileSystemPath.Comparer.Equals(probe.Path, fixture.OutsideMarker)));
            StringAssert.Contains(result.ReplayedCondition, "$(Outside)");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NestedNativeFunction_UsesActualExpandedOperandWithoutChangingSemanticGlobals(bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var expression = "Exists('$([System.IO.Path]::Combine($(Root), $([System.IO.Path]::GetFileName($(NestedMarker)))))')";
            var result = await fixture.ReplayAsync(expression, ["marker.flag"], privateMarker ? ["marker.flag"] : [], unrelatedIo: true);
            Assert.IsTrue(result.SourceSelected, "The nested native function must select the source document before replay.");
            Assert.AreEqual(privateMarker, result.Selected);
            Assert.AreEqual(Path.Combine(fixture.Source, "marker.flag"), result.NativeGlobals["NestedMarker"]);
            Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, "marker.flag"))));
        });
    }

    [TestMethod]
    public async Task LoadedRuntime_CertifiesUncachedProtocolOrPreciselyRefusesProcessWideCache()
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MsBuildCacheFileExistence")))
            {
                // Run this same test in a fresh process with the native switch enabled.
                // No test changes process-wide MSBuild state after initialization.
                var failure = await Assert.ThrowsExactlyAsync<NativeConditionProtocolException>(() =>
                    fixture.ReplayAsync("Exists('$([System.IO.Path]::Combine($(Root), marker.flag))')", ["marker.flag"], ["marker.flag"], unrelatedIo: false));
                Assert.AreEqual(NativeConditionProtocolFailure.ExistenceCache, failure.Failure);
            }
            else
            {
                var result = await fixture.ReplayAsync("Exists('$([System.IO.Path]::Combine($(Root), marker.flag))')",
                    ["marker.flag"], ["marker.flag"], unrelatedIo: true);
                Assert.IsTrue(result.Selected);
            }
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task NativeExistsFileList_PreservesEveryExpandedFile(bool sourceFirst, bool privateSecond)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var result = await fixture.ReplayAsync("Exists('$(Root)/first.flag;$(Root)/second.flag')",
                sourceFirst ? ["first.flag", "second.flag"] : ["second.flag"],
                privateSecond ? ["first.flag", "second.flag"] : ["first.flag"], unrelatedIo: true);
            Assert.AreEqual(sourceFirst, result.SourceSelected, "The native first member controls whether later existence queries run.");
            Assert.AreEqual(privateSecond, result.Selected);
            foreach (var name in new[] { "first.flag", "second.flag" })
                Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, name))));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EscapedSemicolonMarker_RemainsOneNativeFile(bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            const string name = "semi;marker.flag";
            var result = await fixture.ReplayAsync("Exists('$(Root)/semi%3Bmarker.flag')",
                [name], privateMarker ? [name] : [], unrelatedIo: true);
            Assert.IsTrue(result.SourceSelected);
            Assert.AreEqual(privateMarker, result.Selected);
            Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe => FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, name))));
        });
    }

    [TestMethod]
    [DataRow(@"nested\name", "nested/name", false)]
    [DataRow(@"nested\name", "nested/name", true)]
    [DataRow("nested%5Cname", "nested/name", false)]
    [DataRow("nested%5Cname", "nested/name", true)]
    [DataRow("nested%255Cname", "nested%5Cname", false)]
    [DataRow("nested%255Cname", "nested%5Cname", true)]
    public async Task PropertyOperand_UsesNativeTaskItemAndFilesystemCoordinates(string propertyPart, string physicalPart, bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var marker = physicalPart + "/marker.flag";
            var result = await fixture.ReplayPropertyOperandAsync(propertyPart, marker, privateMarker);
            Assert.IsTrue(result.SourceSelected, "The native source predicate must select the independently created marker.");
            Assert.AreEqual(privateMarker, result.Selected);
            Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe =>
                FileSystemPath.Comparer.Equals(probe.Path, Path.GetFullPath(Path.Combine(fixture.Source, marker)))));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LiteralBackslashOperand_UsesNativePreExpansionSeparatorNormalization(bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var result = await fixture.ReplayAsync("Exists('$(Root)\\nested\\marker.flag')",
                ["nested/marker.flag"], privateMarker ? ["nested/marker.flag"] : [], unrelatedIo: true);
            Assert.IsTrue(result.SourceSelected, "Native Exists must resolve the literal backslash operand on this platform.");
            Assert.AreEqual(privateMarker, result.Selected);
            Assert.AreEqual(1, result.Evidence.ConditionProbes.Count(probe =>
                FileSystemPath.Comparer.Equals(probe.Path, Path.Combine(fixture.Source, "nested", "marker.flag"))));
        });
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ExecutedWhenPredicate_ReplaysPrivateInputAtItsOriginalPropertyPass(bool sourceMarker, bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var result = await fixture.ReplayWhenAsync(sourceMarker, privateMarker);
            CollectionAssert.Contains(result.SourceDirectories.ToArray(), Path.Combine(fixture.Source, "selected"));
            Assert.AreEqual(privateMarker, result.PrivateDirectories.Contains(Path.Combine(fixture.Private, "selected"), FileSystemPath.Comparer));
            Assert.IsFalse(result.PrivateDirectories.Contains(Path.Combine(fixture.Source, "selected"), FileSystemPath.Comparer));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnassignedEarlierPathReference_DoesNotBindLaterSemanticAssignment(bool throughAlias)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var result = await fixture.ReplayUnassignedPropertyAsync(throughAlias);
            CollectionAssert.Contains(result.SourceDirectories.ToArray(), Path.Combine(fixture.Source, "output"));
            CollectionAssert.Contains(result.PrivateDirectories.ToArray(), Path.Combine(fixture.Private, "output"));
            Assert.IsFalse(result.PrivateDirectories.Contains(Path.Combine(fixture.Private, "output", "semantic-later-value"), FileSystemPath.Comparer));
        });
    }

    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    public async Task ImportedRelativePredicate_UsesNativeElementSpecificBaseDirectory(bool groupPredicate, bool sourceMarker, bool privateMarker)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var result = await fixture.ReplayImportedRelativeAsync(groupPredicate, sourceMarker, privateMarker);
            CollectionAssert.Contains(result.SourceDirectories.ToArray(), Path.Combine(fixture.Source, "selected"));
            Assert.AreEqual(privateMarker, result.PrivateDirectories.Contains(Path.Combine(fixture.Private, "selected"), FileSystemPath.Comparer),
                "Synthetic relative output coordinates: " + string.Join(",", result.PrivateDirectories.Select(path => Path.GetRelativePath(fixture.Source, path)))
                + "; output declaration origins: " + string.Join(",", result.Evidence.Declarations.Where(declaration => declaration.Name == "OutputPath")
                    .Select(declaration => Path.GetRelativePath(fixture.Source, declaration.FilePath) + ":" + declaration.Expanded)));
            Assert.IsFalse(result.PrivateDirectories.Contains(Path.Combine(fixture.Source, "selected"), FileSystemPath.Comparer));
        });
    }

    [TestMethod]
    public async Task NativeSdkGraph_CapturesExecutedPredicatesWithoutAuthoredConditionFunctions()
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            ReplayResult result;
            try { result = await fixture.ReplaySdkAsync(); }
            catch (NativeConditionProtocolException failure)
            {
                foreach (var key in new[] { "ConditionProfileOwnerMatches", "ConditionProfileCount", "ConditionNativeProjectDepth",
                             "ConditionNativeConditionDepth", "ConditionLatestFrameKind", "ConditionLatestFramePass" })
                    if (failure.Data[key] is string or int or bool) Console.WriteLine(key + "=" + failure.Data[key]);
                throw;
            }
            Assert.IsTrue(result.SourceSelected);
            Assert.IsTrue(result.Selected);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DirectFunctionImport_UsesCapturedNativeTargetAndPrivateDefinition(bool changeSourceAfterCapture)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Source, "imports"));
            Directory.CreateDirectory(Path.Combine(fixture.Private, "imports"));
            var sourcePath = Path.Combine(fixture.Source, "active.proj");
            var importPath = Path.Combine(fixture.Source, "imports", "context.props");
            var definition = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("Import",
                new XAttribute("Project", "$([System.IO.Path]::Combine($(Root), imports/context.props))"))).ToString());
            var imported = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("PropertyGroup",
                new XElement("OutputPath", "$(Root)/captured"))).ToString());
            await File.WriteAllBytesAsync(sourcePath, definition);
            await File.WriteAllBytesAsync(importPath, imported);
            var globals = new Dictionary<string, string> { ["Root"] = fixture.Source };
            var node = EvaluatedBuildGraph.FromRoots([sourcePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            CollectionAssert.Contains(node.PathEvidence.Directories.ToArray(), Path.Combine(fixture.Source, "captured"));
            string Map(string path) => Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, path));
            var privatePath = Path.Combine(fixture.Private, "active.proj");
            await File.WriteAllBytesAsync(privatePath, SolutionFileMutationPlanner.RelocateDefinition(sourcePath, definition,
                Map,
                Map, BuildPathRelocation.KnownPathProperties, node.PathEvidence.Declarations));
            await File.WriteAllBytesAsync(Path.Combine(fixture.Private, "imports", "context.props"),
                SolutionFileMutationPlanner.RelocateDefinition(importPath, imported, Map, Map,
                    BuildPathRelocation.KnownPathProperties, node.PathEvidence.Declarations));
            if (changeSourceAfterCapture)
                await File.WriteAllTextAsync(importPath, "<Project><PropertyGroup><OutputPath>$(Root)/changed-source</OutputPath></PropertyGroup></Project>");
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            CollectionAssert.Contains(actual.PathEvidence.Directories.ToArray(), Path.Combine(fixture.Private, "captured"));
            Assert.IsFalse(actual.PathEvidence.Directories.Any(path => FileSystemPath.IsStrictDescendant(fixture.Source, path)));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NewlyActiveSecondPredicate_RequiresCurrentPolicyBeforeInputHash(bool allowSecondInput)
    {
        await ReplayFixture.RunAsync(async fixture =>
        {
            var deniedDirectory = Path.GetFullPath(Path.Combine(fixture.Source, "..", "denied"));
            Directory.CreateDirectory(fixture.Source);
            Directory.CreateDirectory(fixture.Private);
            Directory.CreateDirectory(deniedDirectory);
            var second = Path.Combine(deniedDirectory, "second.flag");
            await File.WriteAllTextAsync(second, "captured external marker");
            foreach (var directory in new[] { fixture.Source, fixture.Private })
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "global.json"), "{}");
                await File.WriteAllTextAsync(Path.Combine(directory, "A.cs"), "public class Selected { }");
            }
            var sourcePath = Path.Combine(fixture.Source, "active.csproj");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false"),
                    new XElement("EnableDefaultCompileItems", "false")), new XElement("ItemGroup", new XElement("Compile",
                new XAttribute("Include", "A.cs"), new XAttribute("Condition", "Exists('$(Root)/first.flag') And Exists('$(External)/second.flag')")))).ToString());
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var globals = OwnedNativeLoadContext.Properties(sourcePath,
                new Dictionary<string, string> { ["Root"] = fixture.Source, ["External"] = deniedDirectory });
            await PrepareNativeInputsAsync(sourcePath, globals);
            var source = EvaluatedBuildGraph.FromRoots([sourcePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.IsFalse(source.DocumentFiles.Contains(Path.Combine(fixture.Source, "A.cs"), FileSystemPath.Comparer));
            var policy = new BranchInputPolicy([fixture.Source, fixture.Private], deniedDirectory, allowSecondInput);
            var sourceLoad = await new WorkspaceSessionLoader().CreateAndOpenAsync("source-branch", sourcePath, globals, policy,
                new WorkspaceDiagnosticsSink(64), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            using var sourceLease = sourceLoad.Lease;
            using var sourceWorkspace = sourceLoad.Workspace;
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, sourceLoad.Context.Availability);
            Assert.IsFalse(policy.SawSecondInput, "The source predicate must short-circuit before the second physical input.");
            string Map(string path) => Path.Combine(fixture.Private, Path.GetRelativePath(fixture.Source, path));
            var privatePath = Path.Combine(fixture.Private, "active.csproj");
            await File.WriteAllBytesAsync(privatePath, SolutionFileMutationPlanner.RelocateDefinition(sourcePath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, source.PathEvidence.Declarations));
            await File.WriteAllTextAsync(Path.Combine(fixture.Private, "first.flag"), "new private input");
            await PrepareNativeInputsAsync(privatePath, globals);
            var privateNode = EvaluatedBuildGraph.FromRoots([privatePath], globals, ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None).Nodes.Single();
            Assert.IsTrue(privateNode.DocumentFiles.Contains(Path.Combine(fixture.Private, "A.cs"), FileSystemPath.Comparer),
                "The rendered first predicate must consult the private marker despite the unchanged source Root global.");
            Assert.AreEqual(fixture.Source, globals["Root"]);
            // Native Exists can inspect existence under this lock. Any explicit hash
            // before current policy approval instead produces a sharing violation.
            using FileStream? held = allowSecondInput ? null : new(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var privateLoad = await new WorkspaceSessionLoader().CreateAndOpenAsync("private-branch", privatePath, globals, policy,
                new WorkspaceDiagnosticsSink(64), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            using var privateLease = privateLoad.Lease;
            using var privateWorkspace = privateLoad.Workspace;
            Assert.IsTrue(policy.SawSecondInput, "The newly executed operand must enter the fresh authorization inventory.");
            Assert.AreEqual(allowSecondInput ? NativeCompilerContextAvailability.Certified
                : NativeCompilerContextAvailability.BuildDependencyGrantRequired, privateLoad.Context.Availability);
            var normalized = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("private-branch", privateLoad.Context,
                policy, CancellationToken.None);
            if (allowSecondInput)
                await NativeCompilerContextCertificate.Require(normalized.Solution).AssertCurrentAsync(normalized.Solution,
                    "private-branch", policy, CancellationToken.None);
            else
                Assert.ThrowsExactly<RoslynMcp.Core.Services.PublicInvalidOperationException>(() =>
                    NativeCompilerContextCertificate.Require(normalized.Solution));
            held?.Dispose();
            Assert.AreEqual("captured external marker", await File.ReadAllTextAsync(second));

            static async Task PrepareNativeInputsAsync(string path, IReadOnlyDictionary<string, string> properties)
            {
                var directory = Path.GetDirectoryName(path)!;
                await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project />");
                foreach (var arguments in new[] { new[] { "restore", path, "--nologo" }, new[] { "build", path, "--no-restore", "--nologo" } })
                {
                    var execution = await new DotnetCommandRunner().RunAsync(directory, path,
                        MsBuildCommandProperties.Append(arguments, properties), CancellationToken.None);
                    Assert.IsTrue(execution.Succeeded, "The owned SDK inputs must be produced before capturing a native load epoch.");
                }
            }
        });
    }

    private sealed class BranchInputPolicy(IReadOnlyList<string> allowedRoots, string secondRoot, bool allowSecond) : RoslynMcp.Core.Services.IWorkspacePathPolicy
    {
        internal bool SawSecondInput { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var raw in paths)
            {
                var path = Path.GetFullPath(raw);
                var second = FileSystemPath.Comparer.Equals(secondRoot, path) || FileSystemPath.IsStrictDescendant(secondRoot, path);
                SawSecondInput |= second;
                if (second && allowSecond) continue;
                if (!allowedRoots.Any(root => FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path)))
                    throw new UnauthorizedAccessException("The newly active native input is outside this synthetic policy.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed record ReplayResult(bool SourceSelected, bool Selected, string ReplayedCondition, BuildPathRoles.Evidence Evidence,
        IReadOnlyDictionary<string, string> NativeGlobals, IReadOnlyList<string> SourceDirectories, IReadOnlyList<string> PrivateDirectories);

    private sealed record DirectoryQueryObservation(BuildPathRoles.Evidence Evidence, bool[] Selections, string[] QueryPaths);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static DirectoryQueryObservation ObserveDirectoryQuery(string path, Dictionary<string, string> globals)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var observer = new NativeConditionEvidence();
        var collection = observer.Collection;
        Exception? primary = null;
        try
        {
            collection.RegisterLogger(observer);
            observer.VerifyProtocol(collection);
            observer.Begin();
            var project = Microsoft.Build.Evaluation.Project.FromProjectRootElement(
                Microsoft.Build.Construction.ProjectRootElement.Open(path, collection, preserveFormatting: true), observer.Options(globals, collection));
            observer.Bind(project);
            var selections = new List<bool>();
            var queries = new List<string>();
            foreach (var name in new[] { "ReferenceSelected", "Selected" })
            {
                var element = project.Xml.Properties.Single(property => property.Name == name);
                var executions = observer.Executions(element);
                Assert.HasCount(1, executions);
                Assert.HasCount(1, executions[0].Invocations);
                var invocation = executions[0].Invocations[0];
                Assert.HasCount(1, invocation.Inputs);
                var selected = project.GetPropertyValue(name) == "true";
                Assert.AreEqual(invocation.Result, selected, "The native property result must match its own exact observed invocation.");
                Assert.AreEqual(invocation.Inputs[0].Exists, invocation.Result);
                selections.Add(selected);
                queries.Add(invocation.Inputs[0].Path);
            }
            return new(BuildPathRoles.Capture(project, observer), selections.ToArray(), queries.ToArray());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory query observation cleanup failed.", primary, observer);
        }
    }

    private static async Task<bool> AssertDirectoryQueryAdmissionAsync(DirectoryQueryObservation observation, string root)
    {
        var marker = Path.Combine(root, "marker");
        var policy = new OwnedTestWorkspacePathPolicy();
        await policy.ValidateAsync("directory-query-test", [marker], CancellationToken.None);
        var identity = await PhysicalInputIdentity.CaptureAsync(marker, CancellationToken.None);
        Assert.IsNotEmpty(observation.Evidence.ConditionProbes);
        foreach (var probe in observation.Evidence.ConditionProbes)
        {
            Assert.IsTrue(FileSystemPath.Comparer.Equals(marker, Path.TrimEndingDirectorySeparator(Path.GetFullPath(probe.QueryPath))),
                "A source/private operand must bind to that fixture's exact marker.");
            Assert.IsNotNull(probe.ObservedExists, "Every executed Exists operand must retain its native truth.");
            Assert.IsTrue(Path.EndsInDirectorySeparator(probe.QueryPath), "Native expansion and relocation must preserve directory intent.");
        }
        var inconsistent = observation.Evidence.ConditionProbes.Any(probe => probe.ObservedExists != (identity.Kind == PhysicalInputKind.Directory));
        if (inconsistent)
        {
            Assert.ThrowsExactly<RoslynMcp.Core.Services.PublicInvalidOperationException>(() =>
                ConditionInputTruth.AssertObserved(observation.Evidence.ConditionProbes, [identity]),
                "An unstable native observation cannot acquire physical execution authority.");
            return false;
        }
        ConditionInputTruth.AssertObserved(observation.Evidence.ConditionProbes, [identity]);
        return true;
    }
    private sealed class ReplayFixture
    {
        private readonly string _root = Path.Combine(TestTempRoot.Current, "native-condition-protocol", Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(_root, "source");
        internal string Private => Path.Combine(_root, "private");
        internal string Outside => Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, "never-authorized-condition");
        internal string OutsideMarker => Path.Combine(Outside, "marker.flag");

        internal async Task<ReplayResult> ReplayAsync(string expression, string[] sourceMarkers, string[] privateMarkers, bool unrelatedIo)
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Private);
            var children = new List<XElement>();
            if (unrelatedIo)
                children.Add(new XElement("PropertyGroup", new XElement("SemanticRead", "$([System.IO.File]::ReadAllText('$(Root)/unrelated.txt'))")));
            children.Add(new XElement("ItemGroup", new XElement("Compile",
                new XAttribute("Include", "$(Root)/*.cs"), new XAttribute("Condition", expression))));
            return await ReplayDefinitionAsync(new XElement("Project", children), sourceMarkers, privateMarkers, sourceMarkers, ImmutableDictionary<string, string>.Empty);
        }

        internal Task<ReplayResult> ReplayPropertyOperandAsync(string propertyPart, string marker, bool privateMarker) =>
            ReplayDefinitionAsync(new XElement("Project",
                new XElement("PropertyGroup", new XElement("Marker", "$(Root)/" + propertyPart + "/marker.flag")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "$(Root)/*.cs"),
                    new XAttribute("Condition", "Exists('$(Marker)')")))),
                [marker], privateMarker ? [marker] : [], [marker], ImmutableDictionary<string, string>.Empty);

        internal Task<ReplayResult> ReplayWhenAsync(bool sourceMarker, bool privateMarker) =>
            ReplayDefinitionAsync(new XElement("Project", new XElement("Choose",
                new XElement("When", new XAttribute("Condition", "Exists('$(Root)/marker.flag')"),
                    new XElement("PropertyGroup", new XElement("OutputPath", "$(Root)/selected"))))),
                ["marker.flag"], privateMarker ? ["marker.flag"] : [], sourceMarker ? ["marker.flag"] : [], ImmutableDictionary<string, string>.Empty);

        internal Task<ReplayResult> ReplaySdkAsync() => ReplayDefinitionAsync(new XElement("Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("EnableDefaultCompileItems", "false")), new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "A.cs")))),
            [], [], [], ImmutableDictionary<string, string>.Empty);

        internal Task<ReplayResult> ReplayUnassignedPropertyAsync(bool throughAlias)
        {
            var group = new XElement("PropertyGroup", new XAttribute("Condition", "'true' == 'true'"));
            group.Add(new XElement(throughAlias ? "Alias" : "OutputPath", "$(Root)/output/$(Later)"));
            group.Add(new XElement("Later", "semantic-later-value"));
            if (throughAlias) group.Add(new XElement("OutputPath", "$(Alias)"));
            return ReplayDefinitionAsync(new XElement("Project", group), [], [], [], ImmutableDictionary<string, string>.Empty);
        }

        internal Task<ReplayResult> ReplayImportedRelativeAsync(bool groupPredicate, bool sourceMarker, bool privateMarker)
        {
            var output = new XElement("OutputPath", "$(Root)/selected");
            var group = new XElement("PropertyGroup", output);
            (groupPredicate ? group : output).SetAttributeValue("Condition", "Exists('marker.flag')");
            var marker = groupPredicate ? "imports/marker.flag" : "marker.flag";
            return ReplayDefinitionAsync(new XElement("Project", new XElement("Import", new XAttribute("Project", "imports/context.props"))),
                [marker], privateMarker ? [marker] : [], sourceMarker ? [marker] : [],
                new Dictionary<string, string> { ["imports/context.props"] = new XElement("Project", group).ToString() });
        }

        private async Task<ReplayResult> ReplayDefinitionAsync(XElement definition, string[] sourceMarkers,
            string[] privateMarkers, string[] remainingSourceMarkers, IReadOnlyDictionary<string, string> importedDefinitions)
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Private);
            var path = Path.Combine(Source, "active.proj");
            var bytes = Encoding.UTF8.GetBytes(definition.ToString());
            await File.WriteAllBytesAsync(path, bytes);
            await File.WriteAllTextAsync(Path.Combine(Source, "A.cs"), "public class Selected { }");
            await File.WriteAllTextAsync(Path.Combine(Private, "A.cs"), "public class Selected { }");
            await File.WriteAllTextAsync(Path.Combine(Source, "unrelated.txt"), "semantic input");
            foreach (var (name, text) in importedDefinitions)
            {
                var sourcePath = Path.Combine(Source, name);
                var privateDefinition = Path.Combine(Private, name);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(privateDefinition)!);
                await File.WriteAllTextAsync(sourcePath, text);
            }
            foreach (var name in sourceMarkers)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Source, name))!);
                await File.WriteAllTextAsync(Path.Combine(Source, name), "selected");
            }
            foreach (var name in privateMarkers)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Private, name))!);
                await File.WriteAllTextAsync(Path.Combine(Private, name), "selected");
            }
            var globals = new Dictionary<string, string>
            {
                ["Root"] = Source,
                ["Outside"] = Outside,
                ["NestedMarker"] = Path.Combine(Source, "marker.flag")
            };
            var graph = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var evidence = graph.Nodes.Single().PathEvidence;
            foreach (var name in sourceMarkers.Except(remainingSourceMarkers, StringComparer.Ordinal))
                File.Delete(Path.Combine(Source, name));
            string Map(string value) => FileSystemPath.Comparer.Equals(value, Private) || FileSystemPath.IsStrictDescendant(Private, value)
                ? value : Path.Combine(Private, Path.GetRelativePath(Source, value));
            var replay = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations);
            foreach (var (name, text) in importedDefinitions)
                await File.WriteAllBytesAsync(Path.Combine(Private, name),
                    SolutionFileMutationPlanner.RelocateDefinition(Path.Combine(Source, name), Encoding.UTF8.GetBytes(text), Map, Map,
                        BuildPathRelocation.KnownPathProperties, evidence.Declarations));
            var privatePath = Path.Combine(Private, "active.proj");
            await File.WriteAllBytesAsync(privatePath, replay);
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            return new(graph.Nodes.Single().DocumentFiles.Contains(Path.Combine(Source, "A.cs"), FileSystemPath.Comparer),
                actual.DocumentFiles.Contains(Path.Combine(Private, "A.cs"), FileSystemPath.Comparer),
                XDocument.Parse(Encoding.UTF8.GetString(replay)).Descendants().Attributes("Condition").FirstOrDefault()?.Value ?? string.Empty,
                evidence, actual.Globals, evidence.Directories, actual.PathEvidence.Directories);
        }

        internal static async Task RunAsync(Func<ReplayFixture, Task> test)
        {
            var fixture = new ReplayFixture();
            Exception? primary = null;
            try { await test(fixture); }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native condition replay fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture._root)));
            }
        }
    }
}
