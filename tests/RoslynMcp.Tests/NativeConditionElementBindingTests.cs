using RoslynMcp.Roslyn.Helpers;
using System.Runtime.CompilerServices;
using System.Xml;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeConditionElementBindingTests
{
    [TestMethod]
    public void SameLineElements_KeepExactDescriptorsOccurrencesAndSkippedScopes()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        AssertBindings();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertBindings()
    {
        NativeConditionEvidence? evidence = null;
        Exception? primary = null;
        try
        {
            evidence = new NativeConditionEvidence();
            var collection = evidence.Collection;
            collection.RegisterLogger(evidence);
            evidence.VerifyProtocol(collection);
            // One physical line deliberately collides on file, line and element name.
            const string repeated = "<PropertyGroup Condition=\"'true' == 'true'\"><Choice Condition=\"'true' == 'true'\">first</Choice></PropertyGroup>";
            var xml = "<Project>" + repeated + repeated
                + "<PropertyGroup Condition=\"'true' == 'true'\"><Choice Condition=\"'true' == 'true'\">second</Choice></PropertyGroup>"
                + "<PropertyGroup Condition=\"'false' == 'true'\"><Choice Condition=\"'true' == 'true'\">skipped</Choice></PropertyGroup>"
                + "<Target Name=\"NeverExecuted\"><PropertyGroup Condition=\"'true' == 'true'\"><Choice Condition=\"'true' == 'true'\">target</Choice></PropertyGroup></Target></Project>";
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = Microsoft.Build.Construction.ProjectRootElement.Create(reader, collection, preserveFormatting: true);
            root.FullPath = Path.Combine(TestTempRoot.Current, nameof(NativeConditionElementBindingTests), "SameLine.proj");
            evidence.Begin();
            var project = Microsoft.Build.Evaluation.Project.FromProjectRootElement(root, evidence.Options(new Dictionary<string, string>(), collection));
            evidence.Bind(project);
            Assert.AreEqual("second", project.GetPropertyValue("Choice"));
            var groups = root.PropertyGroups.ToArray();
            Assert.AreEqual(4, groups.Length);
            Assert.IsTrue(groups.All(group => group.Location.Line == 1));
            Assert.AreEqual(groups[0].OuterElement, groups[1].OuterElement);
            Assert.AreNotEqual(groups[1].OuterElement, groups[2].OuterElement);
            foreach (var group in groups)
                Assert.HasCount(1, evidence.Executions(group), "Each same-line group must retain its own executed condition occurrence.");
            foreach (var group in groups.Take(3))
                Assert.HasCount(1, evidence.Executions(group.Properties.Single()), "Equal and differing descriptors must bind to their exact parent scope.");
            Assert.IsEmpty(evidence.Executions(groups[3].Properties.Single()));
            var targetGroup = root.Targets.Single().Children.OfType<Microsoft.Build.Construction.ProjectPropertyGroupElement>().Single();
            Assert.IsEmpty(evidence.Executions(targetGroup));
            Assert.IsEmpty(evidence.Executions(targetGroup.Properties.Single()));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native element binding cleanup failed.", primary, evidence); }
    }
}
