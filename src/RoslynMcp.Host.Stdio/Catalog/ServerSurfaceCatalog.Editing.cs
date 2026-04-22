namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] EditingTools =
    [
        Tool("apply_text_edit", "editing", "stable", false, true, "Apply direct text edits to a single file; optional verify + auto-revert on new compile errors."),
        Tool("apply_multi_file_edit", "editing", "experimental", false, true, "Apply direct text edits to multiple files; optional verify + auto-revert on new compile errors."),
        Tool("preview_multi_file_edit", "editing", "experimental", true, false, "Preview a multi-file edit batch; returns per-file diffs and a preview token."),
        Tool("preview_multi_file_edit_apply", "editing", "experimental", false, true, "Apply a previously previewed multi-file edit."),
        Tool("create_file_preview", "file-operations", "stable", true, false, "Preview creating a new source file in a project."),
        Tool("create_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file creation."),
        Tool("delete_file_preview", "file-operations", "stable", true, false, "Preview deleting an existing source file."),
        Tool("delete_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file deletion."),
        Tool("move_file_preview", "file-operations", "stable", true, false, "Preview moving a source file, optionally updating its namespace."),
        Tool("move_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file move."),
        Tool("remove_dead_code_preview", "dead-code", "stable", true, false, "Preview removing unused symbols by handle."),
        Tool("remove_dead_code_apply", "dead-code", "experimental", false, true, "Apply a previously previewed dead-code removal operation."),
        Tool("remove_interface_member_preview", "dead-code", "experimental", true, false, "Composite preview removing a dead interface member and every implementation in one shot. Refuses if any external caller exists."),
        Tool("add_pragma_suppression", "editing", "stable", false, false, "Insert a #pragma warning disable before a line."),
        Tool("pragma_scope_widen", "editing", "stable", false, false, "Extend an existing #pragma warning restore past a target line."),
    ];
}
