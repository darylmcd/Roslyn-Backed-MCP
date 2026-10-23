using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeConditionSerializationTests
{
    [TestMethod]
    public void ReadonlyRoot_PublicOuterElementSupportsInMemoryReloadWithoutSaving()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        ReadonlyRoot();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadonlyRoot()
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(NativeConditionSerializationTests), Guid.NewGuid().ToString("N"));
        ProjectCollection? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "readonly.proj");
            File.WriteAllText(path, """<?xml version="1.0"?><?outside before?><Project><PropertyGroup><Value>A &amp; B</Value></PropertyGroup></Project><!-- after -->""");
            var bytes = File.ReadAllBytes(path);
            owner = new ProjectCollection(null, null, null, ToolsetDefinitionLocations.Local, 1,
                onlyLogCriticalEvents: false, loadProjectsReadOnly: true, useAsynchronousLogging: false, reuseProjectRootElementCache: false);
            var root = ProjectRootElement.Open(path, owner, preserveFormatting: true)!;
            Assert.ThrowsExactly<InvalidOperationException>(() => { _ = root.RawXml; });
            var elementXml = root.OuterElement;
            using var text = new StringReader(elementXml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            root.ReloadFrom(reader, throwIfUnsavedChanges: false, preserveFormatting: true);
            Assert.AreEqual("A & B", root.Properties.Single().Value);
            Assert.IsTrue(root.RawXml.Contains("A &amp; B", StringComparison.Ordinal));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native serialization fixture cleanup failed.", primary, owner,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 2)]
    [DataRow(false, 3)]
    [DataRow(true, 3)]
    [DataRow(false, 4)]
    [DataRow(true, 4)]
    [DataRow(false, 5)]
    [DataRow(true, 5)]
    public void InstrumentedRoot_PreservesIndependentNativeSemanticsAndSourceBytes(bool sameLine, int shape)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        CompareNative(sameLine, shape);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CompareNative(bool sameLine, int shape)
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(NativeConditionSerializationTests), Guid.NewGuid().ToString("N"));
        ProjectCollection? independent = null;
        NativeConditionEvidence? evidence = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "semantics.proj");
            File.WriteAllText(Path.Combine(directory, "present.flag"), "present");
            var separator = sameLine ? "" : "\n";
            var body = string.Join(separator, new[]
            {
                """<?xml version="1.0"?><?outside before?><!-- outside -->""",
                """<Project>""",
                """<!-- inside --><PropertyGroup Condition="Exists('present.flag')"><Value><![CDATA[A < B & C]]></Value></PropertyGroup>""",
                """<ItemGroup Condition="Exists('present.flag')"><None Include="one.txt"><Link>one &amp; two</Link></None></ItemGroup>""",
                """<ItemGroup><MetadataProbe Include="one" Version="" Visible="public" /></ItemGroup>""",
                """<Choose><When Condition="Exists('present.flag')"><PropertyGroup><Chosen>yes</Chosen></PropertyGroup></When><Otherwise><PropertyGroup><Chosen>no</Chosen></PropertyGroup></Otherwise></Choose>""",
                """<ItemGroup Condition="Exists('missing.flag')"><None Include="never.txt" /></ItemGroup>""",
                """<ItemGroup Condition="Exists('present.flag')"><None Include="one.txt"><Link>one &amp; two</Link></None></ItemGroup>""",
                """<UsingTask TaskName="NeverExecuted" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" Condition="'false' == 'true'"><Task><Code Type="Fragment" Language="cs"><![CDATA[Log.LogMessage("A < B & C");]]><!-- body comment --></Code></Task></UsingTask>""",
                """<ProjectExtensions><Payload Kind="public">before<!-- extension comment --><![CDATA[A < B & C]]><Nested />after</Payload></ProjectExtensions>""",
                """</Project><!-- after --><?outside after?>"""
            });
            var valueXml = shape switch
            {
                0 => "<![CDATA[A < B & C]]>",
                1 or 5 => "plain",
                2 => "before<!-- retained comment -->after",
                3 => "before<![CDATA[ < middle & ]]><![CDATA[tail]]>after",
                4 => """<Payload Kind="public">nested &amp; text</Payload>""",
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            var metadataXml = shape is 0 or 5 ? "one &amp; two" : shape == 1 ? "<![CDATA[one < two & three]]>" : valueXml;
            body = body.Replace("<![CDATA[A < B & C]]>", valueXml, StringComparison.Ordinal)
                .Replace("<Link>one &amp; two</Link>", "<Link>" + metadataXml + "</Link>", StringComparison.Ordinal);
            File.WriteAllText(path, body);
            var bytes = File.ReadAllBytes(path);
            independent = new ProjectCollection();
            var original = independent.LoadProject(path);
            Assert.IsFalse(string.IsNullOrEmpty(original.GetPropertyValue("Value")), "Independent native property premise must be nonempty.");
            Assert.IsTrue(original.Xml.AllChildren.OfType<ProjectMetadataElement>()
                .Any(element => element.Name == "Version" && element.Value == "" && element.ExpressedAsAttribute),
                "Independent native construction must retain the empty attribute metadata node.");
            var expectedDeclarations = original.Xml.AllChildren.Select(element => (element.GetType().Name, element.ElementName)).ToArray();
            var expectedMetadata = original.Xml.AllChildren.OfType<ProjectMetadataElement>()
                .Select(element => (element.Name, element.Value, element.ExpressedAsAttribute)).ToArray();
            Assert.AreEqual("", original.GetItems("MetadataProbe").Single().GetMetadataValue("Version"));
            Assert.AreEqual("public", original.GetItems("MetadataProbe").Single().GetMetadataValue("Visible"));
            var expectedItems = original.GetItems("None").Select(item => (item.EvaluatedInclude, item.GetMetadataValue("Link"))).ToArray();
            evidence = new NativeConditionEvidence();
            evidence.Collection.RegisterLogger(evidence);
            evidence.VerifyProtocol(evidence.Collection);
            evidence.Begin();
            var observed = Project.FromProjectRootElement(ProjectRootElement.Open(path, evidence.Collection, preserveFormatting: true)!,
                evidence.Options(new Dictionary<string, string>(), evidence.Collection));
            evidence.Bind(observed);
            _ = BuildPathRoles.Capture(observed, evidence);
            Assert.AreEqual(original.GetPropertyValue("Value"), observed.GetPropertyValue("Value"));
            Assert.AreEqual(original.GetPropertyValue("Chosen"), observed.GetPropertyValue("Chosen"));
            CollectionAssert.AreEqual(expectedDeclarations,
                observed.Xml.AllChildren.Select(element => (element.GetType().Name, element.ElementName)).ToArray(),
                "Every native declaration occurrence and ordinal must match the independent original construction model.");
            CollectionAssert.AreEqual(expectedMetadata, observed.Xml.AllChildren.OfType<ProjectMetadataElement>()
                .Select(element => (element.Name, element.Value, element.ExpressedAsAttribute)).ToArray());
            Assert.AreEqual(original.GetItems("MetadataProbe").Single().GetMetadataValue("Version"),
                observed.GetItems("MetadataProbe").Single().GetMetadataValue("Version"));
            Assert.AreEqual(original.GetItems("MetadataProbe").Single().GetMetadataValue("Visible"),
                observed.GetItems("MetadataProbe").Single().GetMetadataValue("Visible"));
            Assert.AreEqual(original.Xml.AllChildren.OfType<ProjectUsingTaskBodyElement>().Single().TaskBody,
                observed.Xml.AllChildren.OfType<ProjectUsingTaskBodyElement>().Single().TaskBody);
            Assert.AreEqual(original.Xml.AllChildren.OfType<ProjectExtensionsElement>().Single().Content,
                observed.Xml.AllChildren.OfType<ProjectExtensionsElement>().Single().Content);
            CollectionAssert.AreEqual(expectedItems, observed.GetItems("None").Select(item => (item.EvaluatedInclude, item.GetMetadataValue("Link"))).ToArray());
            Assert.AreEqual(2, expectedItems.Length, "Duplicate native item occurrences must remain distinct.");
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native serialization fixture cleanup failed.", primary, evidence, independent,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }
}
