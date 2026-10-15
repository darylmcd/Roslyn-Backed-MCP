namespace SampleLib;

/// <summary>
/// Fixture class for <c>extract_shared_expression_to_helper_preview</c>. The two public
/// methods each contain the same normalization expression
/// <c>System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar)</c>
/// — the concrete repro case from PR #178 (<c>WorkspaceResources.NormalizeFilePathForResource</c>).
/// Extracting the shared expression synthesizes a private static helper and rewrites
/// both call sites, reducing two duplicates to a single canonical definition.
/// </summary>
public sealed class SharedExpressionProbe
{
    public string Load(string filePath)
    {
        // Both methods contain the candidate expression used by ExtractSharedExpressionTests;
        // locate it from the current syntax rather than hardcoded source positions.
        var normalized =
            System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar);
        return normalized;
    }

    public int LoadLineCount(string filePath)
    {
        var normalized =
            System.Uri.UnescapeDataString(filePath).Replace('/', System.IO.Path.DirectorySeparatorChar);
        return normalized.Length;
    }
}
