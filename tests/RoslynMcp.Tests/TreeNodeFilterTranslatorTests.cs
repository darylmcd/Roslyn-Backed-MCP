using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-treenode-filter-translation: translates a single tool-generated VSTest-style
/// FullyQualifiedName atom into MTP's --treenode-filter syntax. The path shape
/// (/{Assembly}/{Namespace}/{Class}/{Method}) is verified against a real TUnit project (see
/// TreeNodeFilterTranslator's doc comment). OR-ing more than one atom is deliberately rejected
/// rather than translated: a real production project on Microsoft.Testing.Platform 2.2.3 was
/// found to silently match ZERO tests for a parenthesized OR-of-literals group, even though the
/// identical syntax worked on a different MTP version — a version-dependent, silently-wrong
/// result too risky to emit.
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
    public void Translate_PropertyNameIsCaseInsensitive()
    {
        var result = TreeNodeFilterTranslator.Translate("fullyqualifiedname=MyNamespace.MyClass.MyMethod");

        Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", result);
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_SameClass_ThrowsRatherThanEmitOrGroup()
    {
        // Confirmed by direct repro against a real production TUnit project: this exact shape
        // — two literal values OR'd within one path segment — silently matched zero tests on
        // Microsoft.Testing.Platform 2.2.3, even though MTP's own grammar permits it and it
        // worked on a different MTP version. Never emit it.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2"));

        StringAssert.Contains(ex.Message, "more than one test");
    }

    [TestMethod]
    public void Translate_TwoAtomsOrdTogether_DifferentClasses_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TreeNodeFilterTranslator.Translate(
                "FullyQualifiedName~MyNamespace.ClassA.Method1|FullyQualifiedName~MyNamespace.ClassB.Method2"));

        StringAssert.Contains(ex.Message, "more than one test");
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
