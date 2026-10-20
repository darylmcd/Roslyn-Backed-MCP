using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Proves a caller-supplied key/value pair serializes as exactly one provider-valid
/// <c>key = value</c> assignment before the writer touches disk. The accepted grammar is the
/// installed public Roslyn parser's (<see cref="AnalyzerConfig.Parse(string, string)"/> consumed
/// through <see cref="AnalyzerConfigSet"/>); no second grammar is defined here.
/// </summary>
internal static class EditorConfigAssignmentValidator
{
    // AnalyzerConfig.Parse requires an absolute path; the probe never touches the file system.
    private static readonly string ProbePath = Path.Combine(Path.GetTempPath(), ".editorconfig");
    private static readonly string ProbeSourcePath = Path.Combine(Path.GetTempPath(), "probe.cs");
    private const string ProbeKey = "roslynmcp_probe_key";
    private const string ProbeValue = "warning";

    private const string KeyRefusal =
        "Key must parse as exactly one editorconfig option name on a single line; line breaks, " +
        "'=' or ':' separators, spaces, section brackets and comment markers are not accepted.";

    private const string ValueRefusal =
        "Value must parse as exactly one editorconfig option value on a single line; line breaks " +
        "and inline '#' or ';' comments are not accepted. Use an empty value or 'unset' to clear an option.";

    /// <summary>Throws <see cref="PublicArgumentException"/> unless the (trimmed) pair is one valid assignment.</summary>
    public static void Validate(string key, string value)
    {
        if (!IsValidKey(key))
        {
            throw new PublicArgumentException(KeyRefusal, nameof(key));
        }

        if (!IsSingleLine(value) || Parse(ProbeKey, value) is not { } valueProbe ||
            !string.Equals(valueProbe.Value, value, StringComparison.OrdinalIgnoreCase))
        {
            throw new PublicArgumentException(ValueRefusal, nameof(value));
        }
    }

    /// <summary>True when <paramref name="key"/> parses as exactly one option name, unchanged.</summary>
    public static bool IsValidKey(string key) =>
        IsSingleLine(key) && Parse(key, ProbeValue) is { } probe &&
        string.Equals(probe.Key, key, StringComparison.OrdinalIgnoreCase);

    // SourceText.Lines is the parser's own line splitter (CR, LF, NEL, LS, PS).
    private static bool IsSingleLine(string text) => SourceText.From(text).Lines.Count == 1;

    /// <summary>
    /// Parses a one-section file holding <c>{key} = {value}</c> and returns the single option the
    /// provider yields, or null when it yields none, several, or any parser diagnostic. A
    /// compiler-routed <c>dotnet_diagnostic.{id}.severity</c> key is reconstructed from its id.
    /// </summary>
    private static (string Key, string Value)? Parse(string key, string value)
    {
        var config = AnalyzerConfig.Parse($"[*]\n{key} = {value}\n", ProbePath);
        var set = AnalyzerConfigSet.Create(System.Collections.Immutable.ImmutableArray.Create(config), out var setDiagnostics);
        var result = set.GetOptionsForSourcePath(ProbeSourcePath);
        if (!setDiagnostics.IsEmpty || !result.Diagnostics.IsEmpty ||
            result.AnalyzerOptions.Count + result.TreeOptions.Count != 1)
        {
            return null;
        }

        if (result.AnalyzerOptions.Count == 1)
        {
            var option = result.AnalyzerOptions.Single();
            return (option.Key, option.Value);
        }

        var tree = result.TreeOptions.Single();
        return ($"dotnet_diagnostic.{tree.Key}.severity", value);
    }
}
