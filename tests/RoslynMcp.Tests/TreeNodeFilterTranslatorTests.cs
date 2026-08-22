using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-treenode-filter-translation: translates the tool-generated VSTest-style
/// FullyQualifiedName filter into MTP's --treenode-filter syntax. The path shape
/// (/{Assembly}/{Namespace}/{Class}/{Method}) and the "OR only within one path segment"
/// constraint are both verified against a real TUnit project (see TestRunnerService's
/// ResolveMtpNativeExecutionPlan doc comment) — these tests cover the translator's parsing
/// and grouping logic in isolation.
/// </summary>
[TestClass]
public sealed class TreeNodeFilterTranslatorTests
{
    [TestMethod]
    public void Translate_SingleFullyQualifiedNameEquals_ProducesFourSegmentPath()
    {
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName=MyNamespace.MyClass.MyMethod");

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_SingleFullyQualifiedNameContains_TreatedSameAsEquals()
    {
        // A complete fqn under "~" behaves identically to "=" here: SynthesizeDotnetTestFilter
        // always emits a full, unique fqn as the value, so "contains" degenerates to an exact
        // per-segment match — there is no partial/fuzzy matching to translate for that shape.
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName~MyNamespace.MyClass.MyMethod");

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_MultiPartNamespace_JoinsWithDots()
    {
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName~Foo.Bar.Baz.MyClass.MyMethod");

        Assert.AreEqual("/*/Foo.Bar.Baz/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_NoNamespace_ClassAndMethodOnly_WildcardsNamespaceSegment()
    {
        // A test class declared in the global namespace has no '.'-separated namespace prefix
        // to recover — wildcard that segment rather than guessing.
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName=MyClass.MyMethod");

        Assert.AreEqual("/*/*/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_SameNamespaceAndClass_MultipleMethods_OrsWithinLastSegment()
    {
        // Exactly the shape TestDiscoveryService.SynthesizeDotnetTestFilter produces for
        // test_related/test_related_files results that land in the same test class.
        var result = TreeNodeFilterTranslator.Translate(
            "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2");

        Assert.AreEqual("/*/MyNamespace/MyClass/(Method1|Method2)", result);
    }

    [TestMethod]
    public void Translate_PropertyNameIsCaseInsensitive()
    {
        var result = TreeNodeFilterTranslator.Translate("fullyqualifiedname=MyNamespace.MyClass.MyMethod");

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_AtomsSpanDifferentClasses_ThrowsWithGroupCount()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.ClassA.Method1|FullyQualifiedName~MyNamespace.ClassB.Method2"));

        StringAssert.Contains(ex.Message, "2 different namespace/class");
    }

    [TestMethod]
    public void Translate_AtomsSpanDifferentNamespaces_SameClassName_ThrowsWithGroupCount()
    {
        // Same class NAME in two different namespaces is still two distinct groups — grouping
        // must key on (namespace, class), not class alone.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~NamespaceA.MyClass.Method1|FullyQualifiedName~NamespaceB.MyClass.Method2"));

        StringAssert.Contains(ex.Message, "2 different namespace/class");
    }

    [TestMethod]
    public void Translate_AndOperator_ThrowsMentioningAnd()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName~Foo.Bar.Baz&TestCategory=Nightly"));

        StringAssert.Contains(ex.Message, "AND");
    }

    [TestMethod]
    public void Translate_ParenthesizedGrouping_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("(FullyQualifiedName~Foo.Bar.Baz)"));

        StringAssert.Contains(ex.Message, "grouping");
    }

    [TestMethod]
    public void Translate_NonFullyQualifiedNameProperty_ThrowsMentioningProperty()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("TestCategory=Nightly"));

        StringAssert.Contains(ex.Message, "FullyQualifiedName");
    }

    [TestMethod]
    public void Translate_NegationOperator_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName!=Foo.Bar.Baz"));

        StringAssert.Contains(ex.Message, "FullyQualifiedName");
    }

    [TestMethod]
    public void Translate_BareValueWithNoDots_ThrowsExplainingMissingClassAndMethod()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName~JustAWord"));

        StringAssert.Contains(ex.Message, "class/method");
    }
}
