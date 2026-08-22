using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-treenode-filter-translation: translates the tool-generated VSTest-style
/// FullyQualifiedName filter into MTP's --treenode-filter syntax. The path shape
/// (/{Assembly}/{Namespace}/{Class}/{Method}) is verified against a real TUnit project (see
/// TreeNodeFilterTranslator's doc comment). OR-ing atoms within one namespace+class is valid
/// MTP grammar and MTP itself matches it correctly (confirmed by the MTP maintainer's own
/// reflection probe, testfx#7300) — but it silently matched zero tests on a real production
/// TUnit project due to an (until recently) open bug in TUnit's own pre-filter
/// (thomhurst/TUnit#6026), fixed in TUnit.Engine 1.46.0. So OR translation is gated on the
/// target project's resolved TUnit.Engine version.
/// </summary>
[TestClass]
public sealed class TreeNodeFilterTranslatorTests
{
    private static readonly Version BelowOrFixThreshold = new(1, 45, 8); // real version this bug was found on
    private static readonly Version AtOrFixThreshold = TreeNodeFilterTranslator.MinimumTUnitEngineVersionWithOrFilterFix;
    private static readonly Version AboveOrFixThreshold = new(1, 65, 38); // real version this was re-verified on

    [TestMethod]
    public void Translate_SingleFullyQualifiedNameEquals_ProducesFourSegmentPath()
    {
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName=MyNamespace.MyClass.MyMethod", resolvedTUnitEngineVersion: null);

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_SingleFullyQualifiedNameContains_TreatedSameAsEquals()
    {
        // A complete fqn under "~" behaves identically to "=" here: SynthesizeDotnetTestFilter
        // always emits a full, unique fqn as the value, so "contains" degenerates to an exact
        // per-segment match — there is no partial/fuzzy matching to translate for that shape.
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName~MyNamespace.MyClass.MyMethod", resolvedTUnitEngineVersion: null);

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_MultiPartNamespace_JoinsWithDots()
    {
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName~Foo.Bar.Baz.MyClass.MyMethod", resolvedTUnitEngineVersion: null);

        Assert.AreEqual("/*/Foo.Bar.Baz/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_NoNamespace_ClassAndMethodOnly_WildcardsNamespaceSegment()
    {
        // A test class declared in the global namespace has no '.'-separated namespace prefix
        // to recover — wildcard that segment rather than guessing.
        var result = TreeNodeFilterTranslator.Translate("FullyQualifiedName=MyClass.MyMethod", resolvedTUnitEngineVersion: null);

        Assert.AreEqual("/*/*/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_PropertyNameIsCaseInsensitive()
    {
        var result = TreeNodeFilterTranslator.Translate("fullyqualifiedname=MyNamespace.MyClass.MyMethod", resolvedTUnitEngineVersion: null);

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_VersionUnknown_ThrowsMentioningTUnitEngine()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
                resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "more than one test");
        StringAssert.Contains(ex.Message, "TUnit.Engine");
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_BelowFixVersion_Throws()
    {
        // The exact version this bug was reproduced on against a real production project.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
                BelowOrFixThreshold));

        StringAssert.Contains(ex.Message, "more than one test");
        StringAssert.Contains(ex.Message, BelowOrFixThreshold.ToString());
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_SameClass_AtFixVersion_EmitsOrGroup()
    {
        var result = TreeNodeFilterTranslator.Translate(
            "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
            AtOrFixThreshold);

        Assert.AreEqual("/*/MyNamespace/MyClass/(Method1|Method2)", result);
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_SameClass_AboveFixVersion_EmitsOrGroup()
    {
        // Re-verified directly against a real TUnit 1.65.38 project.
        var result = TreeNodeFilterTranslator.Translate(
            "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
            AboveOrFixThreshold);

        Assert.AreEqual("/*/MyNamespace/MyClass/(Method1|Method2)", result);
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_DifferentClasses_ThrowsEvenAboveFixVersion()
    {
        // OR across different namespace/class combos is a separate, version-independent MTP
        // grammar limit (OR over full paths, not one path segment) — the TUnit pre-filter fix
        // doesn't touch this at all.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.ClassA.Method1|FullyQualifiedName~MyNamespace.ClassB.Method2",
                AboveOrFixThreshold));

        StringAssert.Contains(ex.Message, "2 different namespace/class");
    }

    [TestMethod]
    public void Translate_AndOperator_ThrowsMentioningAnd()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName~Foo.Bar.Baz&TestCategory=Nightly", resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "AND");
    }

    [TestMethod]
    public void Translate_ParenthesizedGrouping_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("(FullyQualifiedName~Foo.Bar.Baz)", resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "grouping");
    }

    [TestMethod]
    public void Translate_NonFullyQualifiedNameProperty_ThrowsMentioningProperty()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("TestCategory=Nightly", resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "FullyQualifiedName");
    }

    [TestMethod]
    public void Translate_NegationOperator_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName!=Foo.Bar.Baz", resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "FullyQualifiedName");
    }

    [TestMethod]
    public void Translate_BareValueWithNoDots_ThrowsExplainingMissingClassAndMethod()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate("FullyQualifiedName~JustAWord", resolvedTUnitEngineVersion: null));

        StringAssert.Contains(ex.Message, "class/method");
    }
}
