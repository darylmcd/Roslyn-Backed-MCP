namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] SymbolTools =
    [
        Tool("symbol_search", "symbols", "stable", true, false, "Search symbols by name across the workspace."),
        Tool("symbol_info", "symbols", "stable", true, false, "Inspect the symbol at a source location."),
        Tool("go_to_definition", "symbols", "stable", true, false, "Navigate to the symbol definition."),
        Tool("find_references", "symbols", "stable", true, false, "Find references to a symbol."),
        Tool("find_implementations", "symbols", "stable", true, false, "Find implementations of an interface or abstract member."),
        Tool("document_symbols", "symbols", "stable", true, false, "List declared symbols in a document."),
        Tool("find_overrides", "symbols", "stable", true, false, "Find overrides of a virtual or abstract member."),
        Tool("find_base_members", "symbols", "stable", true, false, "Find base or implemented members."),
        Tool("member_hierarchy", "symbols", "stable", true, false, "Summarize base and override relationships for a member."),
        Tool("symbol_signature_help", "symbols", "stable", true, false, "Return symbol signature and documentation."),
        Tool("symbol_relationships", "symbols", "stable", true, false, "Combine definition, reference, base, and implementation relationships."),
        Tool("find_references_bulk", "symbols", "stable", true, false, "Resolve references for multiple symbols in one request."),
        Tool("find_property_writes", "symbols", "stable", true, false, "Find property write sites and classify object-initializer writes."),
        Tool("probe_position", "symbols", "experimental", true, false, "Probe the raw lexical token and containing symbol at a source position."),
        Tool("enclosing_symbol", "symbols", "stable", true, false, "Return the enclosing symbol for a source position."),
        Tool("goto_type_definition", "symbols", "stable", true, false, "Navigate from a symbol usage to its type definition."),
        Tool("get_completions", "symbols", "stable", true, false, "Return IntelliSense-style completion items at a position."),
        Tool("get_symbol_outline", "symbols", "stable", true, false, "Alias for document_symbols (cross-MCP-server name compatibility)."),
    ];
}
