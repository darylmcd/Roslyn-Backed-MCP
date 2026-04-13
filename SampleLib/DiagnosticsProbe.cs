namespace SampleLib;

/// <summary>
/// Holds an intentional compiler warning so integration tests can assert diagnostic bucketing
/// and unsupported fix behavior without coupling to product types like <see cref="Dog"/>.
/// </summary>
internal static class DiagnosticsProbe
{
    private static int _unusedForDiagnostics = 1;
}
