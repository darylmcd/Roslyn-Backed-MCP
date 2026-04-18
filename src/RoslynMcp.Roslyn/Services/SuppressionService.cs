using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Services;

public sealed class SuppressionService : ISuppressionService
{
    private readonly IEditorConfigService _editorConfig;
    private readonly IEditService _editService;
    private readonly IWorkspaceManager? _workspace;
    private readonly ICompileCheckService? _compileCheck;

    /// <summary>
    /// Constructs a <see cref="SuppressionService"/>. The <paramref name="workspace"/> and
    /// <paramref name="compileCheck"/> dependencies are optional: when either is <c>null</c>
    /// the structural pragma-scope operations (<c>AddPragmaWarningDisableAsync</c>,
    /// <c>WidenPragmaScopeAsync</c>, and the directive-scan portion of
    /// <c>VerifyPragmaSuppressesAsync</c>) continue to work, but the fire-site confirmation
    /// inside <c>VerifyPragmaSuppressesAsync</c> — which replays the live compilation to
    /// confirm the target diagnostic is actually reported at the target line — degrades to
    /// <c>null</c> in the <see cref="PragmaVerifyResultDto.DiagnosticFiresAtLine"/> field.
    /// Production DI supplies all four dependencies; stub-driven unit tests that exercise
    /// only the editorconfig and edit-service wiring can omit the workspace pair.
    /// </summary>
    public SuppressionService(
        IEditorConfigService editorConfig,
        IEditService editService,
        IWorkspaceManager? workspace = null,
        ICompileCheckService? compileCheck = null)
    {
        _editorConfig = editorConfig;
        _editService = editService;
        _workspace = workspace;
        _compileCheck = compileCheck;
    }

    public Task<EditorConfigWriteResultDto> SetDiagnosticSeverityAsync(
        string workspaceId, string diagnosticId, string severity, string sourceFilePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id is required.", nameof(diagnosticId));
        }

        var key = $"dotnet_diagnostic.{diagnosticId.Trim()}.severity";
        return _editorConfig.SetOptionAsync(workspaceId, sourceFilePath, key, severity.Trim(), ct);
    }

    public Task<TextEditResultDto> AddPragmaWarningDisableAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct)
    {
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be 1-based and positive.");
        }

        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id is required.", nameof(diagnosticId));
        }

        var pragma = $"#pragma warning disable {diagnosticId.Trim()}{Environment.NewLine}";
        var edit = new TextEditDto(line, 1, line, 1, pragma);
        return _editService.ApplyTextEditsAsync(workspaceId, filePath, [edit], ct);
    }

    public async Task<PragmaVerifyResultDto> VerifyPragmaSuppressesAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct)
    {
        ValidateVerifyWidenArgs(filePath, line, diagnosticId);
        var normalizedId = diagnosticId.Trim();
        var sourceText = await ReadSourceTextAsync(filePath, ct).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var pair = FindEnclosingPragmaPair(root, normalizedId, line);

        // Structural answer first — does a disable/restore pair cover this line?
        bool suppresses;
        string reason;
        int? disableLine = pair.DisableLine;
        int? restoreLine = pair.RestoreLine;

        if (pair.DisableLine is null)
        {
            suppresses = false;
            reason = $"No '#pragma warning disable {normalizedId}' found at or before line {line}.";
        }
        else if (pair.RestoreLine is null)
        {
            // Dangling disable — Roslyn treats this as "disabled until end of file".
            suppresses = line > pair.DisableLine.Value;
            reason = suppresses
                ? $"Dangling '#pragma warning disable {normalizedId}' at line {pair.DisableLine} covers all subsequent lines including {line}."
                : $"Line {line} precedes the disable at line {pair.DisableLine}.";
        }
        else if (line > pair.DisableLine.Value && line < pair.RestoreLine.Value)
        {
            suppresses = true;
            reason = $"Line {line} lies inside the '#pragma warning disable {normalizedId}' span (disable {pair.DisableLine} → restore {pair.RestoreLine}).";
        }
        else if (line <= pair.DisableLine.Value)
        {
            suppresses = false;
            reason = $"Line {line} precedes the disable at line {pair.DisableLine}.";
        }
        else
        {
            // line >= restoreLine
            suppresses = false;
            reason = $"Line {line} is at or past the restore at line {pair.RestoreLine}; the pragma pair (disable {pair.DisableLine} → restore {pair.RestoreLine}) does not cover it.";
        }

        // Optional fire-site confirmation via the live compilation.
        bool? firesAtLine = null;
        if (_workspace is not null && _compileCheck is not null)
        {
            firesAtLine = await TryConfirmDiagnosticFiresAsync(workspaceId, filePath, line, normalizedId, ct).ConfigureAwait(false);
        }

        return new PragmaVerifyResultDto(
            Suppresses: suppresses,
            FilePath: Path.GetFullPath(filePath),
            Line: line,
            DiagnosticId: normalizedId,
            DisableLine: disableLine,
            RestoreLine: restoreLine,
            Reason: reason,
            DiagnosticFiresAtLine: firesAtLine);
    }

    public async Task<PragmaWidenResultDto> WidenPragmaScopeAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct)
    {
        ValidateVerifyWidenArgs(filePath, line, diagnosticId);
        var normalizedId = diagnosticId.Trim();
        var fullPath = Path.GetFullPath(filePath);
        var sourceText = await ReadSourceTextAsync(filePath, ct).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var pair = FindEnclosingPragmaPair(root, normalizedId, line);

        if (pair.DisableLine is null)
        {
            return new PragmaWidenResultDto(
                Success: false,
                FilePath: fullPath,
                TargetLine: line,
                DiagnosticId: normalizedId,
                DisableLine: null,
                OriginalRestoreLine: null,
                NewRestoreLine: null,
                AlreadyCovered: false,
                Reason: $"No '#pragma warning disable {normalizedId}' found at or before line {line}; nothing to widen.");
        }

        // Already-covered: the restore is null (dangling) or strictly past the target line.
        if (pair.RestoreLine is null)
        {
            return new PragmaWidenResultDto(
                Success: true,
                FilePath: fullPath,
                TargetLine: line,
                DiagnosticId: normalizedId,
                DisableLine: pair.DisableLine,
                OriginalRestoreLine: null,
                NewRestoreLine: null,
                AlreadyCovered: true,
                Reason: $"Dangling '#pragma warning disable {normalizedId}' at line {pair.DisableLine} already covers all subsequent lines; no edit needed.");
        }

        if (line < pair.RestoreLine.Value)
        {
            return new PragmaWidenResultDto(
                Success: true,
                FilePath: fullPath,
                TargetLine: line,
                DiagnosticId: normalizedId,
                DisableLine: pair.DisableLine,
                OriginalRestoreLine: pair.RestoreLine,
                NewRestoreLine: pair.RestoreLine,
                AlreadyCovered: true,
                Reason: $"Line {line} already lies inside the pragma span (disable {pair.DisableLine} → restore {pair.RestoreLine}); no edit needed.");
        }

        // Safety invariant: moving the restore from its current line down past `line` must not
        // cross a #region/#endregion boundary or enter another pragma disable for the same id.
        // The scan window is (pair.RestoreLine, line] in ORIGINAL coordinates — any forbidden
        // directive inside that window blocks the widen.
        var safetyBlocker = FindSafetyBlocker(root, normalizedId, pair.RestoreLine.Value, line, pair.RestoreTrivia);
        if (safetyBlocker is not null)
        {
            return new PragmaWidenResultDto(
                Success: false,
                FilePath: fullPath,
                TargetLine: line,
                DiagnosticId: normalizedId,
                DisableLine: pair.DisableLine,
                OriginalRestoreLine: pair.RestoreLine,
                NewRestoreLine: null,
                AlreadyCovered: false,
                Reason: safetyBlocker);
        }

        // Emit two independent edits (delete old restore line, insert new restore at anchor)
        // against ORIGINAL coordinates. EditService.ApplyTextEditsAsync hands them to
        // SourceText.WithChanges in one call, so the spans do not interfere with each other —
        // see BuildRelocateRestoreEdits for the anchor-math rationale.
        var (edits, newRestoreLineInFinalFile) = BuildRelocateRestoreEdits(
            sourceText, pair.RestoreTrivia!, pair.RestoreLine.Value, line, normalizedId);

        var applyResult = await _editService.ApplyTextEditsAsync(
            workspaceId,
            filePath,
            edits,
            ct,
            skipSyntaxCheck: false,
            verify: false,
            autoRevertOnError: false).ConfigureAwait(false);

        if (!applyResult.Success)
        {
            return new PragmaWidenResultDto(
                Success: false,
                FilePath: fullPath,
                TargetLine: line,
                DiagnosticId: normalizedId,
                DisableLine: pair.DisableLine,
                OriginalRestoreLine: pair.RestoreLine,
                NewRestoreLine: null,
                AlreadyCovered: false,
                Reason: "Underlying text edit failed — see apply_text_edit logs for details.");
        }

        return new PragmaWidenResultDto(
            Success: true,
            FilePath: fullPath,
            TargetLine: line,
            DiagnosticId: normalizedId,
            DisableLine: pair.DisableLine,
            OriginalRestoreLine: pair.RestoreLine,
            NewRestoreLine: newRestoreLineInFinalFile,
            AlreadyCovered: false,
            Reason: $"Moved '#pragma warning restore {normalizedId}' from line {pair.RestoreLine} to line {newRestoreLineInFinalFile}; span now covers target line {line}.");
    }

    // ----- helpers -----

    private static void ValidateVerifyWidenArgs(string filePath, int line, string diagnosticId)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be 1-based and positive.");
        }
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id is required.", nameof(diagnosticId));
        }
    }

    private static Task<string> ReadSourceTextAsync(string filePath, CancellationToken ct) =>
        File.ReadAllTextAsync(filePath, ct);

    /// <summary>
    /// Walks the directive trivia list for the file, collects all <c>#pragma warning</c>
    /// directives that mention <paramref name="diagnosticId"/>, and finds the enclosing
    /// pair (most-recent <c>disable</c> at or before <paramref name="line"/>, matching
    /// <c>restore</c> after it). A dangling disable (no matching restore) returns
    /// <see cref="PragmaPair.RestoreLine"/> <c>null</c>.
    /// </summary>
    private static PragmaPair FindEnclosingPragmaPair(SyntaxNode root, string diagnosticId, int line)
    {
        var directives = CollectPragmaDirectives(root, diagnosticId);

        // Find the most-recent disable at or before `line`, then the first restore strictly after it.
        PragmaWarningDirectiveTriviaSyntax? disable = null;
        int disableLine = -1;
        foreach (var d in directives)
        {
            var dLine = d.Directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (d.Kind == PragmaKind.Disable && dLine <= line && dLine > disableLine)
            {
                disable = d.Directive;
                disableLine = dLine;
            }
        }

        if (disable is null)
        {
            return new PragmaPair(null, null, null, null);
        }

        // Find the first matching restore whose line is > disableLine.
        PragmaWarningDirectiveTriviaSyntax? restore = null;
        int restoreLine = int.MaxValue;
        foreach (var d in directives)
        {
            if (d.Kind != PragmaKind.Restore) continue;
            var rLine = d.Directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (rLine > disableLine && rLine < restoreLine)
            {
                restore = d.Directive;
                restoreLine = rLine;
            }
        }

        return new PragmaPair(
            DisableLine: disableLine,
            RestoreLine: restore is null ? null : restoreLine,
            DisableTrivia: disable,
            RestoreTrivia: restore);
    }

    private static List<(PragmaWarningDirectiveTriviaSyntax Directive, PragmaKind Kind)> CollectPragmaDirectives(
        SyntaxNode root, string diagnosticId)
    {
        var results = new List<(PragmaWarningDirectiveTriviaSyntax, PragmaKind)>();
        foreach (var directive in root.DescendantTrivia(descendIntoTrivia: true)
                                      .Where(t => t.HasStructure)
                                      .Select(t => t.GetStructure())
                                      .OfType<PragmaWarningDirectiveTriviaSyntax>())
        {
            if (!DirectiveMentionsId(directive, diagnosticId))
            {
                continue;
            }
            var kind = directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                ? PragmaKind.Disable
                : PragmaKind.Restore;
            results.Add((directive, kind));
        }
        return results;
    }

    private static bool DirectiveMentionsId(PragmaWarningDirectiveTriviaSyntax directive, string diagnosticId)
    {
        // The directive can list multiple ids; match if any of them equal the target id.
        // Tokens can be identifiers (CS0168) or numeric (168) — compare on the textual form.
        foreach (var token in directive.ErrorCodes)
        {
            var text = token.ToString().Trim();
            if (string.Equals(text, diagnosticId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks that relocating the <c>restore</c> from <paramref name="oldRestoreLine"/> so the
    /// pair covers <paramref name="targetLine"/> does not cross a <c>#region</c>/<c>#endregion</c>
    /// boundary or nest into another <c>#pragma warning disable &lt;id&gt;</c> for the same id.
    /// The scan window is <c>(oldRestoreLine .. targetLine + 1]</c> in ORIGINAL line numbers —
    /// lines 1..oldRestoreLine are already inside the pair, and we must also inspect line
    /// <c>targetLine + 1</c> because the new restore will land on that line and colliding with
    /// an existing disable there ("adjacent nesting") is still a scope hazard. Returns a
    /// human-readable reason when the widen is unsafe; <c>null</c> when it is safe.
    /// </summary>
    private static string? FindSafetyBlocker(
        SyntaxNode root,
        string diagnosticId,
        int oldRestoreLine,
        int targetLine,
        PragmaWarningDirectiveTriviaSyntax? existingRestore)
    {
        var windowEnd = targetLine + 1;
        if (oldRestoreLine >= windowEnd)
        {
            return null;
        }

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.HasStructure) continue;
            var structure = trivia.GetStructure();
            if (structure is null) continue;

            var line = structure.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (line <= oldRestoreLine || line > windowEnd) continue;

            switch (structure)
            {
                case RegionDirectiveTriviaSyntax:
                    return $"Widening would cross a '#region' boundary at line {line}.";
                case EndRegionDirectiveTriviaSyntax:
                    return $"Widening would cross an '#endregion' boundary at line {line}.";
                case PragmaWarningDirectiveTriviaSyntax pragma when ReferenceEquals(pragma, existingRestore):
                    // This is the restore we are about to relocate — ignore.
                    continue;
                case PragmaWarningDirectiveTriviaSyntax pragma
                    when pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                         && DirectiveMentionsId(pragma, diagnosticId):
                    return $"Widening would nest into another '#pragma warning disable {diagnosticId}' at line {line}.";
            }
        }
        return null;
    }

    /// <summary>
    /// Produces the edits that move the <c>#pragma warning restore &lt;id&gt;</c> from
    /// <paramref name="oldRestoreLine"/> so the span covers <paramref name="targetLine"/>.
    /// Returns both the edit list (to be applied against the ORIGINAL document by
    /// <see cref="EditService.ApplyTextEditsAsync"/>) and the FINAL-file line number where
    /// the relocated restore ends up, so the caller can round-trip that number through the
    /// <see cref="PragmaWidenResultDto"/>.
    /// </summary>
    /// <remarks>
    /// Implementation note: two independent edits (delete old line + insert at anchor line)
    /// are emitted. Both spans use ORIGINAL coordinates; <see cref="EditService"/> applies
    /// them as a single <see cref="Microsoft.CodeAnalysis.Text.SourceText.WithChanges"/> call
    /// so the two spans do not interfere with each other. Because deleting the old line
    /// removes one line from the file ABOVE the insert anchor, the new restore ends up at
    /// line <c>targetLine + 1</c> of the FINAL file when the anchor is placed at original
    /// line <c>targetLine + 2</c>.
    ///
    /// <para>
    /// Edge case — when the file has no original line at <c>targetLine + 2</c> (e.g. target
    /// is the penultimate line and there is no trailing line after the closing brace), the
    /// insert anchor falls back to the end of the file. The restore is appended.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<TextEditDto> Edits, int NewRestoreLineInFinalFile) BuildRelocateRestoreEdits(
        string sourceText,
        PragmaWarningDirectiveTriviaSyntax oldRestore,
        int oldRestoreLine,
        int targetLine,
        string diagnosticId)
    {
        _ = oldRestore; // retained for symmetry with the pair type; anchor is computed from line number

        // Delete the ENTIRE original restore line including its trailing newline: span is
        // [(oldRestoreLine, col1) .. (oldRestoreLine + 1, col1)).
        var deleteEdit = new TextEditDto(oldRestoreLine, 1, oldRestoreLine + 1, 1, string.Empty);

        // Anchor for the insert. We want the new restore at FINAL line targetLine + 1. Since
        // the delete removes one line above the anchor, picking original line targetLine + 2
        // places the insert at FINAL line targetLine + 1.
        var (anchorLine, anchorColumn, needsLeadingNewline) = ResolveInsertAnchor(sourceText, targetLine + 2);

        // Carry over the indentation of the line the insert sits next to so the pragma is
        // neither wildly out of alignment nor trailing a half-line.
        var indentation = GetIndentationForLine(sourceText, needsLeadingNewline ? targetLine + 1 : targetLine + 2);
        var directiveText = needsLeadingNewline
            ? $"{Environment.NewLine}{indentation}#pragma warning restore {diagnosticId}"
            : $"{indentation}#pragma warning restore {diagnosticId}{Environment.NewLine}";

        var insertEdit = new TextEditDto(anchorLine, anchorColumn, anchorLine, anchorColumn, directiveText);

        // FINAL-file line number of the new restore: the insert falls at FINAL line
        // (anchorLine - 1) when the anchor is past oldRestoreLine. When the anchor falls at
        // end-of-file with a leading newline, the new restore sits one line past the last
        // original line minus the one we deleted.
        int finalRestoreLine;
        if (needsLeadingNewline)
        {
            var totalLinesOriginal = CountLines(sourceText);
            finalRestoreLine = totalLinesOriginal; // after delete (-1) then leading-newline insert (+1) = totalLinesOriginal
        }
        else
        {
            finalRestoreLine = anchorLine - 1;
        }

        return (new TextEditDto[] { deleteEdit, insertEdit }, finalRestoreLine);
    }

    /// <summary>
    /// Computes the <see cref="TextEditDto"/> anchor for an insert that conceptually belongs
    /// at the <b>start</b> of original line <paramref name="desiredLine"/>. When the file has
    /// that many lines, the anchor is <c>(desiredLine, col 1)</c> and the inserted text
    /// carries its own trailing newline. When the file has fewer lines — i.e. the desired
    /// line is past EOF — the anchor is end-of-last-line and the caller is told to prepend a
    /// newline so the inserted directive does not fuse onto the previous line.
    /// </summary>
    private static (int Line, int Column, bool NeedsLeadingNewline) ResolveInsertAnchor(
        string sourceText, int desiredLine)
    {
        var totalLines = CountLines(sourceText);
        if (desiredLine <= totalLines)
        {
            return (desiredLine, 1, NeedsLeadingNewline: false);
        }
        // Desired line is past the last line — anchor at end of the last line, inject a
        // leading newline so the restore sits on its own line.
        var lines = sourceText.Split('\n');
        var lastLine = lines[^1];
        var lastLineEndColumn = lastLine.Length + 1;
        // If the file's trailing char is already '\n' (empty lines[^1]), the anchor is on
        // the phantom last line with column 1. Accept that — Roslyn treats end-of-file as
        // a valid position.
        return (totalLines, lastLineEndColumn, NeedsLeadingNewline: true);
    }

    private static int CountLines(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText)) return 1;
        var lineCount = 1;
        foreach (var ch in sourceText)
        {
            if (ch == '\n') lineCount++;
        }
        // If the file ends with '\n' the last Split('\n') entry is empty — Roslyn counts the
        // position after the trailing newline as its own line, so keep this behaviour.
        return lineCount;
    }

    private static string GetIndentationForLine(string sourceText, int lineNumber)
    {
        var lines = sourceText.Split('\n');
        if (lineNumber < 1 || lineNumber > lines.Length)
        {
            return string.Empty;
        }
        var line = lines[lineNumber - 1];
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }
        return line.Substring(0, i);
    }

    private async Task<bool?> TryConfirmDiagnosticFiresAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct)
    {
        if (_workspace is null || _compileCheck is null)
        {
            return null;
        }

        // Guard against missing/closed workspace — return null rather than throwing so the
        // structural answer still makes it back to the caller.
        if (!_workspace.ContainsWorkspace(workspaceId))
        {
            return null;
        }

        try
        {
            var options = new CompileCheckOptions(FileFilter: filePath, SeverityFilter: null, Limit: 200);
            var result = await _compileCheck.CheckAsync(workspaceId, options, ct).ConfigureAwait(false);
            foreach (var diag in result.Diagnostics)
            {
                if (!string.Equals(diag.Id, diagnosticId, StringComparison.OrdinalIgnoreCase)) continue;
                if (diag.StartLine == line) return true;
                // Allow multi-line spans — treat "line in range" as a hit.
                if (diag.StartLine is int s && diag.EndLine is int e && line >= s && line <= e) return true;
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort confirmation — never let this probe mask the structural answer.
            return null;
        }
    }

    private enum PragmaKind { Disable, Restore }

    private readonly record struct PragmaPair(
        int? DisableLine,
        int? RestoreLine,
        PragmaWarningDirectiveTriviaSyntax? DisableTrivia,
        PragmaWarningDirectiveTriviaSyntax? RestoreTrivia);
}
