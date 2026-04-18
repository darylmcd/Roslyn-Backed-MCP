namespace RoslynMcp.Tests;

/// <summary>
/// position-probe-for-test-fixture-authoring: verifies <c>SymbolNavigationService.ProbePositionAsync</c>
/// returns deterministic, trivia-aware lexical snapshots so fixture authors can pin 1-based
/// line/column anchors without re-implementing Roslyn's token-boundary rules.
/// </summary>
[TestClass]
public sealed class PositionProbeTests : SharedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;
    private static string _animalServicePath = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await LoadSharedSampleWorkspaceAsync();
        var sampleSolutionRoot = Path.GetDirectoryName(SampleSolutionPath)!;
        _animalServicePath = Path.Combine(sampleSolutionRoot, "SampleLib", "AnimalService.cs");
    }

    /// <summary>
    /// Validation row from plan: caret on identifier returns tokenKind=Identifier plus the
    /// enclosing containing symbol (the method the identifier lives in). Uses AnimalService.cs
    /// line 16 "public void MakeThemSpeak(    IEnumerable&lt;IAnimal&gt;     animals   )" —
    /// column 21 lands inside 'MakeThemSpeak' (cols 17-29).
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnIdentifier_ReturnsIdentifierAndContainingSymbol()
    {
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 21, CancellationToken.None);

        Assert.IsNotNull(probe, "Probe must not return null for a file inside the loaded workspace.");
        Assert.AreEqual("Identifier", probe.TokenKind, "Caret inside 'MakeThemSpeak' identifier should classify as Identifier.");
        Assert.AreEqual("IdentifierToken", probe.SyntaxKind);
        Assert.AreEqual("MakeThemSpeak", probe.TokenText);
        Assert.IsFalse(probe.LeadingTriviaBefore, "Caret on the identifier token itself must not report leading-trivia-before.");
        Assert.IsNotNull(probe.ContainingSymbol, "A method declaration identifier has the method itself as its containing symbol.");
        StringAssert.Contains(probe.ContainingSymbol!, "MakeThemSpeak",
            $"Expected containingSymbol to reference MakeThemSpeak; got '{probe.ContainingSymbol}'.");
        Assert.AreEqual("Method", probe.ContainingSymbolKind);
    }

    /// <summary>
    /// Validation row from plan: caret on whitespace returns tokenKind=Whitespace plus the
    /// enclosing containing symbol. AnimalService.cs line 16 parens hold "    " between '(' and
    /// 'IEnumerable' — column 32 lands on whitespace (cols 31-34). The lenient adjacent-identifier
    /// fallback MUST NOT fire: a Whitespace classification here is the whole point of the probe.
    /// Per Roslyn's trivia-attachment rules this whitespace is TRAILING trivia of '(' (not leading
    /// trivia of 'IEnumerable'), so <c>LeadingTriviaBefore</c> is false while <c>TokenKind</c>
    /// still surfaces the Whitespace classification.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnWhitespace_ReturnsWhitespaceAndContainingSymbol()
    {
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 32, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.AreEqual("Whitespace", probe.TokenKind,
            $"Caret on whitespace between '(' and 'IEnumerable' must classify as Whitespace, not as the adjacent identifier. Got tokenText='{probe.TokenText}', syntaxKind='{probe.SyntaxKind}'.");
        Assert.AreEqual("WhitespaceTrivia", probe.SyntaxKind);
        Assert.IsFalse(probe.LeadingTriviaBefore,
            "Trailing whitespace attached to '(' per Roslyn's trivia-attachment rules — not leading trivia of the next token.");
        // Whitespace inside a method declaration's parameter list still resolves to the method as
        // the containing symbol — the probe reports the nearest enclosing member, not a synthesized
        // "none" placeholder.
        Assert.IsNotNull(probe.ContainingSymbol,
            "Whitespace inside a method signature should still report the enclosing method as its containing symbol.");
        StringAssert.Contains(probe.ContainingSymbol!, "MakeThemSpeak");
        Assert.AreEqual("Method", probe.ContainingSymbolKind);
    }

    /// <summary>
    /// Risk row from plan: positions near end-of-line must not accidentally resolve to the next
    /// line's first token. Line 16 ends at column 70 (the closing paren); column 71 is the
    /// newline. Confirm the probe classifies this as <c>EndOfLine</c> trivia — it must NOT leak
    /// forward into line 17's opening brace. Roslyn attaches this EOL trivia to ')' as trailing
    /// trivia, so the containing method is still reported via the enclosing-symbol walk.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnEndOfLine_DoesNotLeakToNextLineToken()
    {
        // Column 71 is the '\n' immediately after ')' on line 16. Roslyn attaches this as
        // EndOfLineTrivia to the ')' token's TRAILING trivia, not the '{' of line 17's leading
        // trivia — the probe must NOT report the next line's token.
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 71, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.AreEqual("EndOfLine", probe.TokenKind,
            $"Caret on line-end whitespace/newline must classify as EndOfLine, not leak forward to the next token. Got tokenKind='{probe.TokenKind}', syntaxKind='{probe.SyntaxKind}', tokenText='{probe.TokenText}'.");
        Assert.AreEqual("EndOfLineTrivia", probe.SyntaxKind);
        Assert.IsFalse(probe.LeadingTriviaBefore,
            "End-of-line trivia terminating a source line is trailing trivia of the preceding token.");
        // Leak check: containing symbol should still be MakeThemSpeak (line 16's declaring method),
        // not MakeThemSpeak's body (which would be resolved if the caret leaked to line 17's '{').
        StringAssert.Contains(probe.ContainingSymbol!, "MakeThemSpeak");
    }

    /// <summary>
    /// Trivia-boundary determinism check: caret EXACTLY on a token boundary (SpanStart of the
    /// identifier, not inside preceding whitespace) must classify as the token — not as the
    /// trailing trivia of the preceding token. This is the primary caret-off-by-one case the
    /// probe is designed to eliminate.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnTokenSpanStart_ClassifiesAsTokenNotTrivia()
    {
        // Line 16, column 35 == SpanStart of 'IEnumerable' (preceded by the 4-space whitespace
        // gap). Roslyn's FindToken returns 'IEnumerable' and position == token.SpanStart means
        // we're on the token, not in its leading trivia.
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 35, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.AreEqual("Identifier", probe.TokenKind,
            $"Caret EXACTLY on a token's start must classify as that token, not as preceding trivia. Got tokenKind='{probe.TokenKind}', tokenText='{probe.TokenText}'.");
        Assert.AreEqual("IEnumerable", probe.TokenText);
        Assert.IsFalse(probe.LeadingTriviaBefore,
            "Caret at token.SpanStart is on the token, not in its leading trivia.");
    }

    /// <summary>
    /// Caret on a keyword token (public) returns <c>Keyword</c> classification. Anchors on
    /// line 16 col 5 — the 'public' modifier of MakeThemSpeak.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnKeyword_ReturnsKeyword()
    {
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 5, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.AreEqual("Keyword", probe.TokenKind);
        Assert.AreEqual("public", probe.TokenText);
        Assert.IsFalse(probe.LeadingTriviaBefore);
    }

    /// <summary>
    /// Leading-trivia detection: caret on the 4-space indent of line 16 (col 1) lands inside the
    /// leading trivia of the 'public' keyword — because the blank line 15 and preceding end-of-line
    /// trivia push this whitespace into the next token's leading-trivia slot. Confirms the probe
    /// distinguishes leading trivia (attached to the following token) from trailing trivia
    /// (attached to the preceding token) via the <c>LeadingTriviaBefore</c> flag.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_CaretOnLineIndent_ReportsLeadingTriviaBeforeFlag()
    {
        // Column 1 on line 16 — start of "    public". The blank line 15 forces the preceding '}'
        // of line 14 to NOT own this whitespace as trailing trivia; instead Roslyn attaches it
        // (plus line 15's EOL) as LEADING trivia of line 16's 'public' keyword.
        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, _animalServicePath, line: 16, column: 1, CancellationToken.None);

        Assert.IsNotNull(probe);
        Assert.AreEqual("Whitespace", probe.TokenKind,
            $"Caret on line-16 indent should classify as Whitespace. Got tokenKind='{probe.TokenKind}', syntaxKind='{probe.SyntaxKind}', tokenText='{probe.TokenText}'.");
        Assert.IsTrue(probe.LeadingTriviaBefore,
            "Indent whitespace after a blank line is leading trivia of the next non-whitespace token.");
    }

    /// <summary>
    /// Out-of-range line surfaces an informative ArgumentException. Keeps the error shape
    /// consistent with <c>GetEnclosingSymbolAsync</c> so clients can share validation logic.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_LineOutOfRange_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            SymbolNavigationService.ProbePositionAsync(
                _workspaceId, _animalServicePath, line: 99_999, column: 1, CancellationToken.None));
    }

    /// <summary>
    /// File not in the loaded workspace returns null — consistent with the rest of the
    /// ISymbolNavigationService surface for missing-document cases.
    /// </summary>
    [TestMethod]
    public async Task ProbePosition_UnknownFile_ReturnsNull()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), "definitely-not-in-the-workspace.cs");

        var probe = await SymbolNavigationService.ProbePositionAsync(
            _workspaceId, bogusPath, line: 1, column: 1, CancellationToken.None);

        Assert.IsNull(probe);
    }
}
