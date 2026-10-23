using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTaskContextEncodingTests
{
    [TestMethod]
    [DataRow("\"")]
    [DataRow("%22")]
    [DataRow("%2522")]
    public void EscapedNativeContextIdentity_MatchesProjectAndEventBoundaries(string semanticValue)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        AssertProjectAndEventBoundaries(semanticValue);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertProjectAndEventBoundaries(string semanticValue)
    {
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        CancellationTokenSource? cancelled = null;
        Exception? primary = null;
        try
        {
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var root = Microsoft.Build.Construction.ProjectRootElement.Create(collection);
            var globals = new Dictionary<string, string> { ["Probe"] = Microsoft.Build.Evaluation.ProjectCollection.Escape(semanticValue) };
            var instance = new Microsoft.Build.Execution.ProjectInstance(root, globals, null, collection);
            Assert.AreEqual(semanticValue, instance.GetPropertyValue("Probe"));
            var started = new Microsoft.Build.Framework.ProjectStartedEventArgs(1, "", "", "", "", null, null,
                Microsoft.Build.Framework.BuildEventContext.Invalid, instance.GlobalProperties, instance.ToolsVersion);
            Assert.IsNotNull(started.ToolsVersion);
            var semantic = NativeTaskExecutionIdentity.ContextIdentity(new Dictionary<string, string> { ["Probe"] = semanticValue }, instance.ToolsVersion, CancellationToken.None);
            Assert.AreEqual(semantic, NativeTaskExecutionIdentity.EscapedContextIdentity(instance.GlobalProperties, instance.ToolsVersion, CancellationToken.None));
            Assert.AreEqual(semantic, NativeTaskExecutionIdentity.EscapedContextIdentity(started.GlobalProperties!, started.ToolsVersion, CancellationToken.None));
            Assert.AreNotEqual(semantic, NativeTaskExecutionIdentity.EscapedContextIdentity(started.GlobalProperties!, "DifferentToolset", CancellationToken.None));
            cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => NativeTaskExecutionIdentity.EscapedContextIdentity(started.GlobalProperties!, started.ToolsVersion, cancelled.Token));

            var effective = NativeCompilerTaskInputs.EffectiveGlobals(globals, semanticValue);
            Assert.AreEqual(semanticValue, Microsoft.Build.Evaluation.ProjectCollection.Unescape(effective["TargetFramework"]),
                "Evaluated framework selection must enter the escaped-global boundary exactly once.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primary, cancelled, collection); }
    }

    [TestMethod]
    [DataRow("quote", true)]
    [DataRow("escape-case", true)]
    [DataRow("changed-value", false)]
    [DataRow("changed-key", false)]
    [DataRow("literal-percent", false)]
    public void EscapedNativeContextIdentity_NormalizesOnlyTheMsBuildBoundary(string shape, bool equivalent)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        AssertEscapedIdentity(shape, equivalent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertEscapedIdentity(string shape, bool equivalent)
    {
        var expected = new Dictionary<string, string> { ["CurrentSolutionConfigurationContents"] = "<Solution Configuration=\"Debug\" />" };
        var actual = new Dictionary<string, string> { ["CurrentSolutionConfigurationContents"] = "<Solution Configuration=%22Debug%22 />" };
        switch (shape)
        {
            case "quote": break;
            case "escape-case": expected["Probe"] = "%3b"; actual["Probe"] = "%3B"; break;
            case "changed-value": actual["CurrentSolutionConfigurationContents"] = "<Solution Configuration=%22Release%22 />"; break;
            case "changed-key": actual = new() { ["AnotherConfiguration"] = actual.Values.Single() }; break;
            case "literal-percent": expected["Probe"] = "%2522"; actual["Probe"] = "%22"; break;
            default: Assert.Fail("Unknown controlled escaped context shape."); break;
        }
        var before = NativeTaskExecutionIdentity.EscapedContextIdentity(expected, "Current", CancellationToken.None);
        var after = NativeTaskExecutionIdentity.EscapedContextIdentity(actual, "Current", CancellationToken.None);
        if (equivalent) Assert.AreEqual(before, after);
        else Assert.AreNotEqual(before, after, "Different semantic global values or names must retain distinct authority identities.");

        var escapedLiteral = new Dictionary<string, string> { ["Probe"] = "%2522" };
        var literal = new Dictionary<string, string> { ["Probe"] = "%22" };
        Assert.AreEqual(NativeTaskExecutionIdentity.ContextIdentity(literal, "Current", CancellationToken.None),
            NativeTaskExecutionIdentity.EscapedContextIdentity(escapedLiteral, "Current", CancellationToken.None),
            "Exactly one escaped-boundary decode must preserve a literal percent sequence.");
        Assert.AreNotEqual(NativeTaskExecutionIdentity.ContextIdentity(literal, "Current", CancellationToken.None),
            NativeTaskExecutionIdentity.ContextIdentity(new Dictionary<string, string> { ["Probe"] = "\"" }, "Current", CancellationToken.None),
            "Semantic identity hashing must never decode already evaluated values.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeContextIdentity_BindsTheEffectiveToolsetWithNativeNameCasing(bool sameToolset)
    {
        var globals = new Dictionary<string, string> { ["Probe"] = "ordinary" };
        var before = NativeTaskExecutionIdentity.ContextIdentity(globals, "ControlledToolset", CancellationToken.None);
        var after = NativeTaskExecutionIdentity.ContextIdentity(globals,
            sameToolset ? "controlledtoolset" : "AnotherControlledToolset", CancellationToken.None);
        if (sameToolset) Assert.AreEqual(before, after);
        else Assert.AreNotEqual(before, after);
    }

    [TestMethod]
    [DataRow("unpaired-high")]
    [DataRow("unpaired-low")]
    [DataRow("replacement")]
    [DataRow("key-case")]
    [DataRow("framing")]
    public void NativeContextIdentity_BindsExactCodeUnitsAndNativeKeyComparison(string shape)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        AssertIdentity(shape);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertIdentity(string shape)
    {
        var left = new Dictionary<string, string> { ["Probe"] = "ordinary" };
        var right = new Dictionary<string, string> { ["Probe"] = "ordinary" };
        switch (shape)
        {
            case "unpaired-high": left["Probe"] = "\ud800"; right["Probe"] = "\ud801"; break;
            case "unpaired-low": left["Probe"] = "\udc00"; right["Probe"] = "\udc01"; break;
            case "replacement": left["Probe"] = "\ud800"; right["Probe"] = "\ufffd"; break;
            case "key-case": right = new() { ["pRoBe"] = "ordinary" }; break;
            case "framing": left = new() { ["AB"] = "C" }; right = new() { ["A"] = "BC" }; break;
            default: Assert.Fail("Unknown controlled context encoding shape."); break;
        }
        var before = NativeTaskExecutionIdentity.GlobalsIdentity(left, CancellationToken.None);
        var after = NativeTaskExecutionIdentity.GlobalsIdentity(right, CancellationToken.None);
        if (shape == "key-case") Assert.AreEqual(before, after);
        else Assert.AreNotEqual(before, after, "Distinct framed code-unit identities must not collapse through encoding replacement.");
    }
}
