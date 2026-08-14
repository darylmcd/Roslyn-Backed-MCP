using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class EditTools
{

    [McpServerTool(Name = "apply_text_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, true,
        "Apply direct text edits to a single file; optional verify + auto-revert on new compile errors."),
     Description("Apply one or more text edits to a source file in the workspace. Each edit specifies a range (start/end line and column) and the replacement text. The workspace is updated in-place and a diff is returned. Revertible via revert_last_apply (one snapshot per call, single-slot per workspace). When verify=true, runs compile_check scoped to the owning project after the edit and attaches the new-error set as Verification (pre-existing errors are filtered out via a pre-vs-post fingerprint diff). When autoRevertOnError=true AND new errors appeared, the edit is rolled back through the same single-slot undo path this call just populated. Prefer a semantic preview/apply Roslyn tool whenever one exists; only fall back to apply_text_edit when no semantic equivalent is available.")]
    public static Task<string> ApplyTextEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to edit")] string filePath,
        [Description("Array of text edits. Each edit has startLine, startColumn, endLine, endColumn (1-based), and newText. Example: [{\"startLine\":10,\"startColumn\":5,\"endLine\":10,\"endColumn\":15,\"newText\":\"newValue\"}]. All positions are 1-based inclusive; non-overlapping edits are applied in the order given.")] TextEditDto[] edits,
        CancellationToken ct = default,
        [Description("When false (default), C# files are parsed after edits and lexer/parser errors block the apply. Set true only for intentional intermediate invalid states.")] bool skipSyntaxCheck = false,
        [Description("When true, run compile_check scoped to the owning project after the edit and attach the result under Verification. Pre-existing errors are filtered out, so only NEW errors appear in the outcome. Default false preserves the original call-site behavior.")] bool verify = false,
        [Description("When true AND verify surfaces new compile errors, automatically revert the edit through the single-slot undo path this call populated. Single-shot per call - never touches prior-turn edits. Ignored when verify is false. Default false.")] bool autoRevertOnError = false)
    {
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            // path-boundary-link-swap-toctou: pin the canonical (link-resolved) target the boundary
            // check actually approved and write to THAT, so a symlink/junction swap between this
            // validation and the physical write cannot redirect the bytes outside the boundary.
            var canonicalWritePath = await ClientRootPathValidator
                .ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);

            // ...but ONLY when the pinned target is the same file the edit service will resolve.
            // The two use different algorithms: the document lookup matches lexically on
            // Path.GetFullPath (which collapses ".." without touching the filesystem), while the
            // canonical target is walked physically and deliberately does NOT collapse ".." first
            // (ConfiguredRootBoundary.ResolvePathCore). For a request shaped
            // "<root>/real/sub/link/../Program.cs" those resolve to DIFFERENT physical files, so
            // pinning would write the edited document's text over an unrelated file — and the undo
            // snapshot records the document, leaving the clobbered file unrecoverable.
            // Fail closed on divergence, consistent with the repo's stance on ambiguous paths.
            EnsurePinnedTargetMatchesResolvedDocument(filePath, canonicalWritePath);
            var result = await editService.ApplyTextEditsAsync(workspaceId, filePath, edits, "apply_text_edit", c, skipSyntaxCheck, verify, autoRevertOnError, canonicalWritePath);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    /// <summary>
    /// Refuses to pin a canonical write target that does not denote the same physical file the
    /// edit service will resolve the document to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EditService</c> finds the document by matching <see cref="Path.GetFullPath(string)"/> —
    /// a purely lexical normalization that collapses <c>..</c> without consulting the filesystem.
    /// The boundary's canonical target is produced by walking the path physically, and
    /// <c>ConfiguredRootBoundary.ResolvePathCore</c> deliberately does NOT collapse <c>..</c>
    /// lexically first, because doing so would change the physical target of a path such as
    /// <c>allowed/link-to-outside/../secret.cs</c>.
    /// </para>
    /// <para>
    /// Both behaviors are individually correct; they simply disagree whenever a <c>..</c> segment
    /// follows a link. Re-resolving the lexically-normalized path physically and comparing tells us
    /// whether this request is one of those: equal means the two algorithms agree and the pin is
    /// safe, unequal means the write target and the edited document are different files.
    /// </para>
    /// <para>
    /// Failing closed is deliberate. Silently falling back to the un-pinned path would reopen the
    /// validation-to-use race this pin exists to close, and honoring the pin anyway would write one
    /// file's contents over another with the undo snapshot pointing at the wrong file.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The lexically-resolved and physically-resolved targets denote different files.
    /// </exception>
    internal static void EnsurePinnedTargetMatchesResolvedDocument(string filePath, string canonicalWritePath)
    {
        var documentTarget = ClientRootPathValidator.ResolvePath(Path.GetFullPath(filePath));
        if (string.Equals(documentTarget, canonicalWritePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ArgumentException(
            $"Path '{filePath}' resolves ambiguously: the document it identifies " +
            $"('{documentTarget}') is not the file the boundary check approved " +
            $"('{canonicalWritePath}'). This happens when a '..' segment follows a symlink or " +
            "junction, where lexical and physical resolution disagree. Re-issue the request with a " +
            "path that does not traverse a link via '..'.",
            nameof(filePath));
    }
}
