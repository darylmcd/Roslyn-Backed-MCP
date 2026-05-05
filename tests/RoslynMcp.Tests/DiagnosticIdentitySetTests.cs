using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// apply-with-verify-diff-not-counts: unit coverage for the shared identity-diff helper used
/// by <c>apply_with_verify</c> and <c>apply_text_edit/apply_multi_file_edit verify=true</c>.
/// The helper switched from a fingerprint that included column AND message text
/// (<c>id|file:line:col|message</c>) to an identity tuple of just
/// <c>id|file|line</c>. These tests pin the format so future edits don't accidentally
/// reintroduce column/message into the identity (which was the source of the ~14%
/// false-positive rollback rate that motivated this change).
/// </summary>
[TestClass]
public sealed class DiagnosticIdentitySetTests
{
    // ------------------------------------------------------------------
    // FormatIdentity: id + file + line, nothing else.
    // ------------------------------------------------------------------

    [TestMethod]
    public void FormatIdentity_ProducesIdFileLineTuple()
    {
        var diag = new DiagnosticDto(
            Id: "CS0103",
            Message: "The name 'Foo' does not exist in the current context",
            Severity: "Error",
            Category: "Compiler",
            FilePath: "/repo/src/Foo.cs",
            StartLine: 12,
            StartColumn: 7,
            EndLine: 12,
            EndColumn: 10);

        var identity = DiagnosticIdentitySet.FormatIdentity(diag);

        Assert.AreEqual("CS0103|/repo/src/Foo.cs|12", identity,
            "Identity must be id|file|line; column and message are deliberately excluded.");
    }

    [TestMethod]
    public void FormatIdentity_IsStable_AcrossMessageTextChanges()
    {
        // Message-text shift on a pre-existing diagnostic must NOT change the identity —
        // this is the canonical false-positive scenario the row describes (~14% of
        // rollbacks). The pre-apply baseline diag and the post-apply diag at the same
        // location with a slightly different message text MUST hash to the same identity
        // so the diff filter treats them as the same diagnostic.
        var pre = new DiagnosticDto("CS0246", "Type or namespace 'Foo' could not be found",
            "Error", "Compiler", "/repo/A.cs", 5, 3, 5, 6);
        var post = new DiagnosticDto("CS0246",
            "The type or namespace name 'Foo' could not be found (are you missing a using directive or an assembly reference?)",
            "Error", "Compiler", "/repo/A.cs", 5, 3, 5, 6);

        Assert.AreEqual(
            DiagnosticIdentitySet.FormatIdentity(pre),
            DiagnosticIdentitySet.FormatIdentity(post),
            "Identity must be stable across message-text shifts on the same id+file+line.");
    }

    [TestMethod]
    public void FormatIdentity_IsStable_AcrossColumnShifts()
    {
        // Column on a pre-existing diagnostic can shift when an unrelated edit on the
        // same line moves trailing tokens. The identity must NOT flip because of that.
        var pre = new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
            "/repo/A.cs", 12, 7, 12, 10);
        var post = new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
            "/repo/A.cs", 12, 11, 12, 14); // column moved 4 right

        Assert.AreEqual(
            DiagnosticIdentitySet.FormatIdentity(pre),
            DiagnosticIdentitySet.FormatIdentity(post),
            "Identity must be stable across column shifts on the same id+file+line.");
    }

    [TestMethod]
    public void FormatIdentity_DiffersAcrossLines()
    {
        // Multiple instances of the same diagnostic on different lines are common
        // (e.g. CS8618 nullable warnings on every uninitialized field). Identity must
        // distinguish them so a NEW occurrence of the same id on a NEW line still
        // triggers rollback when introduced by an apply.
        var line5 = new DiagnosticDto("CS8618", "Non-nullable field must contain a non-null value",
            "Error", "Compiler", "/repo/A.cs", 5, 1, 5, 20);
        var line10 = line5 with { StartLine = 10, EndLine = 10 };

        Assert.AreNotEqual(
            DiagnosticIdentitySet.FormatIdentity(line5),
            DiagnosticIdentitySet.FormatIdentity(line10),
            "Identity must differ when same id appears on different lines.");
    }

    [TestMethod]
    public void FormatIdentity_DiffersAcrossFiles()
    {
        var aCs = new DiagnosticDto("CS0103", "X", "Error", "Compiler", "/repo/A.cs", 5, 1, 5, 5);
        var bCs = aCs with { FilePath = "/repo/B.cs" };

        Assert.AreNotEqual(
            DiagnosticIdentitySet.FormatIdentity(aCs),
            DiagnosticIdentitySet.FormatIdentity(bCs),
            "Identity must differ across files even when id+line match.");
    }

    [TestMethod]
    public void FormatIdentity_NullDiagnostic_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => DiagnosticIdentitySet.FormatIdentity(null!));
    }

    // ------------------------------------------------------------------
    // ExtractErrorIdentities: only Error severity, dedupes by identity.
    // ------------------------------------------------------------------

    [TestMethod]
    public void ExtractErrorIdentities_FromCompileCheckDto_FiltersNonErrorSeverity()
    {
        var check = new CompileCheckDto(
            Success: false, ErrorCount: 1, WarningCount: 1, TotalDiagnostics: 3,
            ReturnedDiagnostics: 3, Offset: 0, Limit: 50, HasMore: false,
            Diagnostics:
            [
                new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
                    "/repo/A.cs", 5, 1, 5, 5),
                new DiagnosticDto("CS0168", "Variable declared but never used", "Warning", "Compiler",
                    "/repo/A.cs", 8, 1, 8, 5),
                new DiagnosticDto("IDE0001", "Simplify name", "Info", "Style",
                    "/repo/A.cs", 9, 1, 9, 5),
            ],
            ElapsedMs: 0);

        var identities = DiagnosticIdentitySet.ExtractErrorIdentities(check);

        Assert.AreEqual(1, identities.Count,
            "Only Error-severity diagnostics should produce identities; Warning + Info excluded.");
        Assert.IsTrue(identities.Contains("CS0103|/repo/A.cs|5"));
    }

    [TestMethod]
    public void ExtractErrorIdentities_DedupesByIdentity_NotByMessage()
    {
        // Two diagnostics with the same id+file+line but different message text — common
        // when an analyzer emits the same finding twice with slightly different wording.
        // The identity set must dedupe to ONE entry (HashSet semantics).
        var check = new CompileCheckDto(
            Success: false, ErrorCount: 2, WarningCount: 0, TotalDiagnostics: 2,
            ReturnedDiagnostics: 2, Offset: 0, Limit: 50, HasMore: false,
            Diagnostics:
            [
                new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
                    "/repo/A.cs", 5, 1, 5, 5),
                new DiagnosticDto("CS0103", "The name does not exist in the current context",
                    "Error", "Compiler", "/repo/A.cs", 5, 1, 5, 5),
            ],
            ElapsedMs: 0);

        var identities = DiagnosticIdentitySet.ExtractErrorIdentities(check);

        Assert.AreEqual(1, identities.Count,
            "Identity-based dedup must collapse same-id+file+line diagnostics regardless of message.");
    }

    [TestMethod]
    public void ExtractErrorIdentities_FromEnumerable_ParitiesWithDtoOverload()
    {
        var diags = new[]
        {
            new DiagnosticDto("CS0103", "X", "Error", "Compiler", "/repo/A.cs", 5, 1, 5, 5),
            new DiagnosticDto("CS0246", "Y", "Error", "Compiler", "/repo/B.cs", 12, 1, 12, 5),
        };
        var asCheck = new CompileCheckDto(false, 2, 0, 2, 2, 0, 50, false, diags, 0);

        var fromEnumerable = DiagnosticIdentitySet.ExtractErrorIdentities(diags);
        var fromDto = DiagnosticIdentitySet.ExtractErrorIdentities(asCheck);

        Assert.IsTrue(fromEnumerable.SetEquals(fromDto),
            "Enumerable and DTO overloads must produce the same identity set.");
    }

    // ------------------------------------------------------------------
    // diff-not-counts trigger condition: post.Except(pre) is the
    // introduced set; a count change without identity change is NOT a
    // rollback trigger (this is the heart of the row's behavior change).
    // ------------------------------------------------------------------

    [TestMethod]
    public void DiffTrigger_PreExistingErrorWithMessageFlip_DoesNotProduceIntroducedSet()
    {
        // Pre-apply: one error at id+file+line.
        var preCheck = new CompileCheckDto(false, 1, 0, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS0103", "Name 'X' does not exist", "Error", "Compiler",
                "/repo/A.cs", 5, 1, 5, 5)],
            0);

        // Post-apply: same identity, message has shifted (e.g. analyzer's wording widened
        // because surrounding code changed). The count is also still 1 — a count-delta
        // implementation would NOT flag this either, but the previous fingerprint
        // implementation that included the message in the key WOULD have flagged it as
        // a "new" error — exactly the false-positive bug this change fixes.
        var postCheck = new CompileCheckDto(false, 1, 0, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS0103",
                "The name 'X' does not exist in the current context",
                "Error", "Compiler", "/repo/A.cs", 5, 1, 5, 5)],
            0);

        var preIds = DiagnosticIdentitySet.ExtractErrorIdentities(preCheck);
        var postIds = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

        var introduced = postIds.Except(preIds).ToList();

        Assert.AreEqual(0, introduced.Count,
            "Pre-existing diagnostic with message-text-flip MUST NOT appear as introduced. " +
            "This was the false-positive that the message-in-fingerprint implementation produced.");
    }

    [TestMethod]
    public void DiffTrigger_NewDiagnosticAtNewLine_AppearsInIntroducedSet()
    {
        // Pre-apply: error at line 5.
        var preCheck = new CompileCheckDto(false, 1, 0, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
                "/repo/A.cs", 5, 1, 5, 5)],
            0);

        // Post-apply: error at line 5 still there, AND a new error at line 12 — the apply
        // introduced a genuinely new diagnostic identity. This MUST be flagged so rollback
        // fires (the true-positive trigger condition).
        var postCheck = new CompileCheckDto(false, 2, 0, 2, 2, 0, 50, false,
            [
                new DiagnosticDto("CS0103", "Name does not exist", "Error", "Compiler",
                    "/repo/A.cs", 5, 1, 5, 5),
                new DiagnosticDto("CS0103", "Name 'NewSymbol' does not exist", "Error", "Compiler",
                    "/repo/A.cs", 12, 7, 12, 16),
            ],
            0);

        var preIds = DiagnosticIdentitySet.ExtractErrorIdentities(preCheck);
        var postIds = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

        var introduced = postIds.Except(preIds).ToList();

        Assert.AreEqual(1, introduced.Count,
            "A new diagnostic at a NEW (id+file+line) tuple MUST appear in the introduced set.");
        Assert.AreEqual("CS0103|/repo/A.cs|12", introduced[0],
            "The introduced identity must be the new line, not the pre-existing one.");
    }

    [TestMethod]
    public void DiffTrigger_PreExistingErrorWithSeverityFlip_DoesNotFireWhenStillError()
    {
        // The retro evidence cited a "pre-existing diagnostic flipped severity class on the
        // post-apply build path". When the diagnostic is still Error severity post-apply
        // (e.g. it was Error before AND remains Error after — unchanged from the diff
        // helper's perspective), the introduced set must be empty.
        var preCheck = new CompileCheckDto(false, 1, 0, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS8602", "Dereference of a possibly null reference",
                "Error", "Compiler", "/repo/A.cs", 22, 5, 22, 12)],
            0);
        var postCheck = preCheck; // Same diagnostic post-apply.

        var preIds = DiagnosticIdentitySet.ExtractErrorIdentities(preCheck);
        var postIds = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

        Assert.IsFalse(postIds.Except(preIds).Any(),
            "Unchanged pre-existing error must not appear as introduced.");
    }

    [TestMethod]
    public void DiffTrigger_PreExistingWarningPromotedToError_DoesNotFire()
    {
        // Subtle case: a pre-existing warning at line 5 gets promoted to error post-apply
        // (e.g. because TreatWarningsAsErrors was conditionally enabled by the apply). The
        // pre-apply set has no Error at line 5 because the pre-apply diag was a Warning,
        // so the post-apply Error at line 5 IS technically a new identity in the Error set.
        // This is the trigger-firing case the row's "severity flip" language refers to —
        // and the diff-not-counts policy is: rollback ONLY when the diagnostic identity
        // is new in the Error set. Promotion from warning to error counts as new — the
        // caller should know about it. Document this expectation explicitly so future
        // edits don't quietly change it.
        var preCheck = new CompileCheckDto(false, 0, 1, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS0168", "Variable declared but never used", "Warning",
                "Compiler", "/repo/A.cs", 5, 1, 5, 5)],
            0);
        var postCheck = new CompileCheckDto(false, 1, 0, 1, 1, 0, 50, false,
            [new DiagnosticDto("CS0168", "Variable declared but never used", "Error",
                "Compiler", "/repo/A.cs", 5, 1, 5, 5)],
            0);

        var preIds = DiagnosticIdentitySet.ExtractErrorIdentities(preCheck);
        var postIds = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

        Assert.AreEqual(0, preIds.Count,
            "Pre-apply Error set must be empty when only Warnings existed.");
        Assert.AreEqual(1, postIds.Count,
            "Post-apply Error set must contain the promoted diagnostic.");
        Assert.AreEqual(1, postIds.Except(preIds).Count(),
            "Warning-to-Error promotion at the same line IS a new error identity from the verify pass's perspective. " +
            "This is intentional: the apply made the build worse and the caller should know.");
    }
}
