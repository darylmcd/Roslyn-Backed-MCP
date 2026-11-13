namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Diagnostic scaffolding for the ubuntu-26.04 silent exit-1 family
/// (row <c>linux-2604-fixture-child-silent-exit-1</c>): fixture-owned <c>dotnet restore</c>,
/// <c>dotnet build</c> and <c>dotnet msbuild</c> children otherwise exit 1 with only
/// "Determining projects to restore..." or "Build FAILED. 0 Warning(s) 0 Error(s)" and an empty
/// stderr. Detailed verbosity makes the failing task print its own reason. Apply only to
/// children whose stdout is not parsed and whose argument list is not asserted; remove this
/// type when the row closes.
/// </summary>
internal static class FixtureChildDiagnostics
{
    internal const string DetailedVerbosity = "-v:detailed";

    internal static string[] Detailed(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return [.. arguments, DetailedVerbosity];
    }
}
