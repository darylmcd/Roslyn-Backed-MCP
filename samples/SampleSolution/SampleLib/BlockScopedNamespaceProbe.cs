namespace SampleLib.BlockScoped
{
    /// <summary>
    /// Deliberately declares a BLOCK-scoped namespace. Every other file in this fixture uses a
    /// file-scoped namespace, and the repository <c>.editorconfig</c> sets
    /// <c>csharp_style_namespace_declarations = file_scoped:warning</c>, so this file is the only
    /// IDE0161 occurrence in the sample solution.
    ///
    /// <para>
    /// That is its entire purpose: <c>PreviewRouteBindingFileOpsTests.FixAllPreview_RecordsItsOwnProducerKind</c>
    /// needs a diagnostic that (a) has a registered FixAll provider and (b) actually occurs in the
    /// fixture, so that <c>fix_all_preview</c> mints a real preview token whose recorded
    /// <c>PreviewKind</c> can be asserted. Without an occurrence, <c>FixAllService</c> returns an
    /// empty token at its zero-diagnostic guard and the round-trip assertion is unreachable.
    /// </para>
    ///
    /// <para>
    /// Do NOT convert this to a file-scoped namespace, and do not "fix" the IDE0161 warning here.
    /// <c>SampleLib.csproj</c> sets <c>TreatWarningsAsErrors=false</c> and <c>samples/</c> is not part
    /// of <c>RoslynMcp.slnx</c>, so the warning is inert outside the tests that want it.
    /// </para>
    /// </summary>
    public static class BlockScopedNamespaceProbe
    {
        public static string Describe() => "block-scoped";
    }
}
