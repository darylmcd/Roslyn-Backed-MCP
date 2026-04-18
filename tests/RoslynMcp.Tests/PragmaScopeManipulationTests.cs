using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Integration coverage for the <c>verify_pragma_suppresses</c> / <c>pragma_scope_widen</c>
/// tool pair (initiative <c>pragma-directive-scope-manipulation</c>). The canonical scenario
/// mirrors the IT-Chat-Bot RetrievalExecutor CA2025 bug: a pragma pair wraps the wrong span
/// while the diagnostic actually fires further down. Verify must report the mismatch; widen
/// must move the restore so the subsequent verify passes.
/// </summary>
[TestClass]
public sealed class PragmaScopeManipulationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static SuppressionService CreateService() =>
        new SuppressionService(EditorConfigService, EditService, WorkspaceManager, CompileCheckService);

    // ------------------------------------------------------------------
    // Canonical misaligned-pair scenario. The fixture is deliberately CS0219-rich (unused
    // local vars) so the live compilation reports the diagnostic at the fire line, giving
    // us end-to-end coverage of both the structural and the fire-site branches in one test.
    //
    // Line layout in the original file (1-based):
    //   1: namespace SampleLib.PragmaFixture;
    //   2: (blank)
    //   3: public class Misaligned
    //   4: {
    //   5:     public void M()
    //   6:     {
    //   7: #pragma warning disable CS0219
    //   8:         int wrapped = 1;                         <-- covered
    //   9: #pragma warning restore CS0219
    //  10:         int leaked = 2;                          <-- fire site, NOT covered
    //  11:     }
    //  12: }
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Verify_ThenWiden_ThenVerify_CoversFireSite()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var filePath = workspace.GetPath("SampleLib", "Pragma_MisalignedPair.cs");
        var fixtureText = """
            namespace SampleLib.PragmaFixture;

            public class Misaligned
            {
                public void M()
                {
            #pragma warning disable CS0219
                    int wrapped = 1;
            #pragma warning restore CS0219
                    int leaked = 2;
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, fixtureText, CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();

        // Step 1 — verify at line 10 (the leaked declaration, OUTSIDE the pair):
        // Structurally NOT suppressed, and the compiler reports CS0219 there.
        var verifyBefore = await sut.VerifyPragmaSuppressesAsync(
            workspace.WorkspaceId, filePath, line: 10, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(verifyBefore.Suppresses,
            $"Verify should report the pair does NOT cover line 10. Reason: {verifyBefore.Reason}");
        Assert.AreEqual(7, verifyBefore.DisableLine);
        Assert.AreEqual(9, verifyBefore.RestoreLine);
        Assert.IsTrue(verifyBefore.DiagnosticFiresAtLine == true,
            $"Compiler should confirm CS0219 fires at line 10. DiagnosticFiresAtLine={verifyBefore.DiagnosticFiresAtLine}");

        // Step 2 — widen the restore past line 10.
        var widen = await sut.WidenPragmaScopeAsync(
            workspace.WorkspaceId, filePath, line: 10, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(widen.Success, $"Widen must succeed. Reason: {widen.Reason}");
        Assert.IsFalse(widen.AlreadyCovered, "The original pair did not cover line 10, so this is a real widen.");
        Assert.AreEqual(7, widen.DisableLine);
        Assert.AreEqual(9, widen.OriginalRestoreLine);
        Assert.IsNotNull(widen.NewRestoreLine);
        Assert.IsTrue(widen.NewRestoreLine.Value > 10,
            $"NewRestoreLine ({widen.NewRestoreLine}) must be strictly past line 10 so the span covers it.");

        // Reload so the post-edit source text is what verify observes.
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        // Step 3 — verify again. The pair now covers line 10.
        var verifyAfter = await sut.VerifyPragmaSuppressesAsync(
            workspace.WorkspaceId, filePath, line: 10, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(verifyAfter.Suppresses,
            $"Verify should now report the pair covers line 10. Reason: {verifyAfter.Reason}");
        Assert.AreEqual(7, verifyAfter.DisableLine);
        Assert.AreEqual(widen.NewRestoreLine, verifyAfter.RestoreLine);
        // Fire-site confirmation should now be false — the compiler no longer reports CS0219
        // at line 10 because the widened pair suppresses it.
        Assert.IsTrue(verifyAfter.DiagnosticFiresAtLine == false,
            $"After widen the compiler should no longer fire CS0219 at line 10. DiagnosticFiresAtLine={verifyAfter.DiagnosticFiresAtLine}");
    }

    // ------------------------------------------------------------------
    // No pragma pair at all → verify returns Suppresses=false with a clear reason.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Verify_NoPragmaPair_ReturnsFalse()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_NoPair.cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            namespace SampleLib.PragmaFixture;

            public class NoPair
            {
                public void M()
                {
                    int unused = 1;
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        var result = await sut.VerifyPragmaSuppressesAsync(
            workspace.WorkspaceId, filePath, line: 7, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Suppresses);
        Assert.IsNull(result.DisableLine);
        Assert.IsNull(result.RestoreLine);
        StringAssert.Contains(result.Reason, "No '#pragma warning disable CS0219' found");
    }

    // ------------------------------------------------------------------
    // Widen with no disable anywhere → fails with a clear reason.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Widen_NoPragmaPair_ReturnsFailureUntouched()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_WidenNoPair.cs");
        var originalText = """
            namespace SampleLib.PragmaFixture;

            public class NoPair
            {
                public void M()
                {
                    int unused = 1;
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, originalText, CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        var result = await sut.WidenPragmaScopeAsync(
            workspace.WorkspaceId, filePath, line: 7, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Reason, "No '#pragma warning disable CS0219' found");

        // The file must be unchanged since widen refused to act.
        var afterText = await File.ReadAllTextAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(originalText.Replace("\r\n", "\n"), afterText.Replace("\r\n", "\n"),
            "Widen must not modify the file when it refuses to act.");
    }

    // ------------------------------------------------------------------
    // Already-covered: widen is a no-op when the existing pair already wraps the target.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Widen_AlreadyCovered_IsNoOp()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_AlreadyCovered.cs");
        var originalText = """
            namespace SampleLib.PragmaFixture;

            public class AlreadyCovered
            {
                public void M()
                {
            #pragma warning disable CS0219
                    int a = 1;
                    int b = 2;
                    int c = 3;
            #pragma warning restore CS0219
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, originalText, CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        // Target line 9 (int b = 2) is inside the pair (disable=7, restore=11).
        var result = await sut.WidenPragmaScopeAsync(
            workspace.WorkspaceId, filePath, line: 9, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.AlreadyCovered, "Widen should short-circuit when the pair already covers the target.");
        Assert.AreEqual(7, result.DisableLine);
        Assert.AreEqual(11, result.OriginalRestoreLine);

        // File contents must be unchanged.
        var afterText = await File.ReadAllTextAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(originalText.Replace("\r\n", "\n"), afterText.Replace("\r\n", "\n"));
    }

    // ------------------------------------------------------------------
    // Safety invariant: widening must refuse when the new restore would cross a '#region'
    // or '#endregion' boundary. The file stays untouched.
    //
    //   1: namespace ...
    //   2: blank
    //   3: public class CrossingRegion
    //   4: {
    //   5:   public void M()
    //   6:   {
    //   7: #pragma warning disable CS0219
    //   8:       int wrapped = 1;
    //   9: #pragma warning restore CS0219
    //  10: #region Tail
    //  11:       int tailed = 2;
    //  12: #endregion
    //  13:   }
    //  14: }
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Widen_CrossingRegionBoundary_IsRefused()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_CrossingRegion.cs");
        var originalText = """
            namespace SampleLib.PragmaFixture;

            public class CrossingRegion
            {
                public void M()
                {
            #pragma warning disable CS0219
                    int wrapped = 1;
            #pragma warning restore CS0219
            #region Tail
                    int tailed = 2;
            #endregion
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, originalText, CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        // Target line 11 (int tailed = 2) is inside the #region Tail / #endregion block.
        // Widening pair (7..9) to cover line 11 would cross the #region boundary at line 10.
        var result = await sut.WidenPragmaScopeAsync(
            workspace.WorkspaceId, filePath, line: 11, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Success, $"Widen must refuse to cross a #region boundary. Reason: {result.Reason}");
        StringAssert.Contains(result.Reason, "region");
        Assert.IsNull(result.NewRestoreLine);

        var afterText = await File.ReadAllTextAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(originalText.Replace("\r\n", "\n"), afterText.Replace("\r\n", "\n"),
            "Widen must not modify the file when it refuses to act.");
    }

    // ------------------------------------------------------------------
    // Safety invariant: widening must refuse when the target line falls BETWEEN two
    // pragma pairs for the same id, i.e. the new restore would land right on (or past)
    // a subsequent disable directive for the same id.
    //
    //   1: namespace ...
    //   2: blank
    //   3: public class NestedDisable
    //   4: {
    //   5:   public void M()
    //   6:   {
    //   7: #pragma warning disable CS0219
    //   8:       int wrapped = 1;
    //   9: #pragma warning restore CS0219
    //  10:       int between = 2;       <-- target, between the two pairs
    //  11: #pragma warning disable CS0219
    //  12:       int later = 3;
    //  13: #pragma warning restore CS0219
    //  14:   }
    //  15: }
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Widen_NestingIntoAnotherDisable_IsRefused()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_NestedDisable.cs");
        var originalText = """
            namespace SampleLib.PragmaFixture;

            public class NestedDisable
            {
                public void M()
                {
            #pragma warning disable CS0219
                    int wrapped = 1;
            #pragma warning restore CS0219
                    int between = 2;
            #pragma warning disable CS0219
                    int later = 3;
            #pragma warning restore CS0219
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, originalText, CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        // Target line 10 (int between = 2) is NOT covered by either pair (first ends at
        // line 9, second starts at line 11). Widening the first pair past line 10 would
        // put the new restore at line 11 — the same line as the second disable → refuse.
        var result = await sut.WidenPragmaScopeAsync(
            workspace.WorkspaceId, filePath, line: 10, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Success, $"Widen must refuse to nest into another disable. Reason: {result.Reason}");
        StringAssert.Contains(result.Reason, "nest");

        var afterText = await File.ReadAllTextAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(originalText.Replace("\r\n", "\n"), afterText.Replace("\r\n", "\n"));
    }

    // ------------------------------------------------------------------
    // Fire-site confirmation: verify surfaces DiagnosticFiresAtLine=true when the live
    // compilation reports the diagnostic at the given line.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Verify_DiagnosticFiresAtLine_IsTrue_WhenCompilerReportsIt()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var filePath = workspace.GetPath("SampleLib", "Pragma_FireSite.cs");
        // CS0219 fires on the unused local at line 7.
        await File.WriteAllTextAsync(
            filePath,
            """
            namespace SampleLib.PragmaFixture;

            public class FireSite
            {
                public void M()
                {
                    int unused = 1;
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var sut = CreateService();
        var result = await sut.VerifyPragmaSuppressesAsync(
            workspace.WorkspaceId, filePath, line: 7, diagnosticId: "CS0219", CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Suppresses, "No pair → structurally not suppressing.");
        Assert.IsTrue(result.DiagnosticFiresAtLine == true,
            $"Compiler should report CS0219 at line 7. DiagnosticFiresAtLine={result.DiagnosticFiresAtLine}");
    }

    // ------------------------------------------------------------------
    // Argument validation.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task VerifyPragmaSuppresses_Throws_OnBadArgs()
    {
        var sut = CreateService();
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            sut.VerifyPragmaSuppressesAsync("ws", "/tmp/a.cs", 0, "CS0219", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.VerifyPragmaSuppressesAsync("ws", "/tmp/a.cs", 1, " ", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.VerifyPragmaSuppressesAsync("ws", " ", 1, "CS0219", CancellationToken.None));
    }

    [TestMethod]
    public async Task PragmaScopeWiden_Throws_OnBadArgs()
    {
        var sut = CreateService();
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            sut.WidenPragmaScopeAsync("ws", "/tmp/a.cs", 0, "CS0219", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.WidenPragmaScopeAsync("ws", "/tmp/a.cs", 1, " ", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.WidenPragmaScopeAsync("ws", " ", 1, "CS0219", CancellationToken.None));
    }
}
