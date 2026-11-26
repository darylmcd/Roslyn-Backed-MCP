using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Builds the client-facing form of a test run without mutating the full-fidelity execution
/// record used by parsers and server diagnostics.
/// </summary>
internal static partial class TestRunPublicProjection
{
    internal const string RedactedValue = "<redacted>";
    internal const string RedactedResultsDirectory = "<results-directory>";
    internal const string RedactedExternalPath = "<external-path>";

    public static PublicCommandExecutionDto CreateExecution(CommandExecutionDto execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return new ExecutionRedactor(execution).CreatePublicExecution();
    }

    public static PublicTestRunResultDto Create(TestRunResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var redactor = new ExecutionRedactor(result.Execution);
        var failureEnvelope = result.FailureEnvelope is null
            ? null
            : result.FailureEnvelope with
            {
                Summary = redactor.RedactText(result.FailureEnvelope.Summary),
                StdOutTail = redactor.RedactOptionalText(result.FailureEnvelope.StdOutTail, result.FailureEnvelope.PublicStdOutTail),
                StdErrTail = redactor.RedactOptionalText(result.FailureEnvelope.StdErrTail, result.FailureEnvelope.PublicStdErrTail),
            };
        var failures = result.Failures
            .Select(failure => failure with
            {
                DisplayName = redactor.MaskFilter(failure.DisplayName, failure.PublicDisplayName),
                FullyQualifiedName = failure.FullyQualifiedName is null ? null : redactor.MaskFilter(failure.FullyQualifiedName),
                Message = redactor.RedactText(failure.Message, failure.PublicMessage),
                StackTrace = redactor.RedactOptionalText(failure.StackTrace, failure.PublicStackTrace),
            })
            .ToArray();

        return new PublicTestRunResultDto(
            redactor.CreatePublicExecution(),
            result.Total,
            result.Passed,
            result.Failed,
            result.Skipped,
            failures,
            failureEnvelope,
            result.Warnings,
            result.CommandDurationMs);
    }

    private readonly record struct TextPair(string Raw, string Safe);

    private sealed class ExecutionRedactor
    {
        private readonly CommandExecutionDto _execution;
        private readonly TestFilterRedaction _filters;
        private readonly List<(string Value, string Replacement, int SourceStart)> _replacements = [];
        private readonly string[] _publicArguments;
        private readonly string _publicWorkingDirectory;
        private readonly string _publicTargetPath;

        public ExecutionRedactor(CommandExecutionDto execution)
        {
            _execution = execution;
            _filters = new TestFilterRedaction(execution.Arguments);
            _publicTargetPath = ProjectOwnTarget(execution.TargetPath);
            _publicWorkingDirectory = IsFullyQualifiedPath(execution.WorkingDirectory)
                ? "." : _filters.Mask(execution.WorkingDirectory);
            _publicArguments = ProjectArguments(execution.Arguments);
            var target = ProjectTargetPath(execution.TargetPath);
            AddPathReplacement(execution.TargetPath, target.Value, target.Start);
            AddPathReplacement(execution.WorkingDirectory, IsFullyQualifiedPath(execution.WorkingDirectory) ? "." : execution.WorkingDirectory, -1);
            _replacements.Sort(static (left, right) => right.Value.Length.CompareTo(left.Value.Length));
        }

        public PublicCommandExecutionDto CreatePublicExecution() =>
            new(_execution.Command, _publicArguments, _publicWorkingDirectory, _publicTargetPath,
                _execution.ExitCode, _execution.Succeeded, _execution.DurationMs,
                RedactText(_execution.StdOut, _execution.PublicStdOut),
                RedactText(_execution.StdErr, _execution.PublicStdErr),
                RedactOptionalText(_execution.EarlyKillReason));

        public string MaskFilter(string value, string? safe = null) => safe ?? _filters.Mask(value);

        public string RedactText(string value, string? safe = null)
        {
            var pair = DiagnosticPathProjector.Project(new TextPair(value, safe ?? _filters.Mask(value)), _execution.WorkingDirectory);
            foreach (var (key, replacement, sourceStart) in _replacements)
            {
                var raw = new StringBuilder();
                var masked = new StringBuilder();
                var consumed = 0;
                while (consumed <= pair.Raw.Length - key.Length)
                {
                    var match = pair.Raw.IndexOf(key, consumed, StringComparison.OrdinalIgnoreCase);
                    if (match < 0) break;
                    raw.Append(pair.Raw.AsSpan(consumed, match - consumed));
                    masked.Append(pair.Safe.AsSpan(consumed, match - consumed));
                    raw.Append(replacement);
                    masked.Append(sourceStart < 0 ? replacement : TransportMask(replacement, pair.Safe.AsSpan(match + sourceStart, replacement.Length)));
                    consumed = match + key.Length;
                }
                raw.Append(pair.Raw.AsSpan(consumed));
                masked.Append(pair.Safe.AsSpan(consumed));
                pair = new TextPair(raw.ToString(), masked.ToString());
            }
            return pair.Safe;
        }

        public string? RedactOptionalText(string? value, string? safe = null) => value is null ? null : RedactText(value, safe);

        private string[] ProjectArguments(IReadOnlyList<string> arguments)
        {
            var projected = arguments.ToArray();
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (TestFilterRedaction.TryGetOption(argument, out var inlineFilter))
                {
                    if (inlineFilter is not null)
                        projected[index] = argument[..(argument.IndexOf('=') + 1)] + RedactedValue;
                    else if (index + 1 < arguments.Count)
                        projected[++index] = RedactedValue;
                    continue;
                }
                if (string.Equals(argument, "--results-directory", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
                {
                    var directory = arguments[++index];
                    projected[index] = RedactedResultsDirectory;
                    AddPathReplacement(directory, RedactedResultsDirectory, -1);
                    continue;
                }
                if (argument.StartsWith("--results-directory=", StringComparison.OrdinalIgnoreCase))
                {
                    projected[index] = argument[..(argument.IndexOf('=') + 1)] + RedactedResultsDirectory;
                    AddPathReplacement(argument[(argument.IndexOf('=') + 1)..], RedactedResultsDirectory, -1);
                    continue;
                }
                if (PathsEqual(argument, _execution.TargetPath))
                {
                    var recipe = ProjectTargetPath(argument);
                    var canonical = ProjectTargetPath(_execution.TargetPath).Value;
                    projected[index] = recipe.Start < 0 ? canonical : TransportMask(canonical, _filters.Mask(argument).AsSpan(recipe.Start, canonical.Length));
                    continue;
                }
                if (IsFullyQualifiedPath(argument))
                {
                    projected[index] = "<path>";
                    AddPathReplacement(argument, projected[index], -1);
                }
            }
            return projected;
        }

        private string ProjectOwnTarget(string target)
        {
            var recipe = ProjectTargetPath(target);
            return recipe.Start < 0 ? recipe.Value : TransportMask(recipe.Value, _filters.Mask(target).AsSpan(recipe.Start, recipe.Value.Length));
        }

        private static string TransportMask(string canonical, ReadOnlySpan<char> safe)
        {
            var result = canonical.ToCharArray();
            for (var index = 0; index < result.Length; index++)
                if (safe[index] == '*') result[index] = '*';
            return new string(result);
        }

        private void AddPathReplacement(string value, string replacement, int sourceStart)
        {
            if (!IsFullyQualifiedPath(value)) return;
            Add(value);
            Add(value.Replace('\\', '/'));
            Add(value.Replace('/', '\\'));
            void Add(string key)
            {
                if (key.Length < 2 || key == replacement || _replacements.Any(entry => string.Equals(entry.Value, key, StringComparison.OrdinalIgnoreCase))) return;
                _replacements.Add((key, replacement, sourceStart));
            }
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        private static (string Value, int Start) ProjectTargetPath(string target)
        {
            if (!IsFullyQualifiedPath(target)) return (target, 0);
            var normalized = target.TrimEnd('/', '\\').Replace('\\', '/');
            var separator = normalized.LastIndexOf('/');
            return separator >= 0 && separator + 1 < normalized.Length ? (normalized[(separator + 1)..], separator + 1) : ("<target>", -1);
        }

        private static bool IsFullyQualifiedPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Path.IsPathFullyQualified(value) || value[0] == '/'
                || (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\');
        }
    }

    /// <summary>
    /// Projects filesystem-shaped text without relying on the current host operating system.
    /// Child output can contain Windows, UNC, and POSIX paths regardless of where the server runs.
    /// </summary>
    private static partial class DiagnosticPathProjector
    {
        public static TextPair Project(TextPair value, string workspaceRoot)
        {
            if (value.Raw.Length == 0) return value;
            var projected = ProjectMatches(CompilerLocationPathRegex(), value, workspaceRoot);
            projected = ProjectMatches(StackTracePathRegex(), projected, workspaceRoot);
            projected = ProjectMatches(DoubleQuotedPathRegex(), projected, workspaceRoot);
            projected = ProjectMatches(SingleQuotedPathRegex(), projected, workspaceRoot);
            return ProjectMatches(BarePathRegex(), projected, workspaceRoot);
        }

        private static TextPair ProjectMatches(Regex regex, TextPair value, string workspaceRoot)
        {
            var raw = new StringBuilder();
            var safe = new StringBuilder();
            var consumed = 0;
            foreach (Match match in regex.Matches(value.Raw))
            {
                var group = match.Groups["path"];
                var length = group.Length;
                while (length > 0 && IsTrailingPunctuation(group.Value[length - 1])) length--;
                var candidate = group.Value[..length].TrimEnd();
                if (!TryParseAbsolutePath(candidate, out var path)) continue;
                var replacement = TryParseAbsolutePath(workspaceRoot, out var workspace)
                    && path.TryMakeRelativeTo(workspace, out var relativeSegments)
                        ? new TextPair(relativeSegments.Length == 0 ? "." : string.Join('/', relativeSegments.Select(segment => segment.Text)),
                            relativeSegments.Length == 0 ? "." : string.Join('/', relativeSegments.Select(segment => value.Safe.Substring(group.Index + segment.Start, segment.Length))))
                        : new TextPair(RedactedExternalPath, RedactedExternalPath);
                raw.Append(value.Raw.AsSpan(consumed, group.Index - consumed));
                safe.Append(value.Safe.AsSpan(consumed, group.Index - consumed));
                raw.Append(replacement.Raw);
                safe.Append(replacement.Safe);
                consumed = group.Index + candidate.Length;
            }
            raw.Append(value.Raw.AsSpan(consumed));
            safe.Append(value.Safe.AsSpan(consumed));
            return new TextPair(raw.ToString(), safe.ToString());
        }

        private static bool TryParseAbsolutePath(string value, out ParsedAbsolutePath path)
        {
            path = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.Replace('\\', '/');
            string root;
            int remainderStart;
            bool ignoreCase;
            if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/')
            {
                root = normalized[..3];
                remainderStart = 3;
                ignoreCase = true;
            }
            else if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                var roots = Tokenize(normalized, 2).ToArray();
                if (roots.Length < 2) return false;
                root = "//" + roots[0].Text + "/" + roots[1].Text;
                remainderStart = roots[1].Start + roots[1].Length;
                ignoreCase = true;
            }
            else if (normalized[0] == '/')
            {
                root = "/";
                remainderStart = 1;
                ignoreCase = false;
            }
            else return false;
            var segments = new List<PathSegment>();
            foreach (var segment in Tokenize(normalized, remainderStart))
            {
                if (segment.Text == ".") continue;
                if (segment.Text == "..")
                {
                    if (segments.Count == 0) return false;
                    segments.RemoveAt(segments.Count - 1);
                }
                else segments.Add(segment);
            }
            path = new ParsedAbsolutePath(root, segments.ToArray(), ignoreCase);
            return true;
        }

        private static IEnumerable<PathSegment> Tokenize(string value, int start)
        {
            while (start < value.Length)
            {
                if (value[start] == '/') { start++; continue; }
                var end = value.IndexOf('/', start);
                if (end < 0) end = value.Length;
                yield return new PathSegment(value[start..end], start, end - start);
                start = end;
            }
        }

        private readonly record struct PathSegment(string Text, int Start, int Length);

        private static bool IsTrailingPunctuation(char value) =>
            value is '.' or ',' or ';' or ':' or ')' or ']' or '}';

        private readonly record struct ParsedAbsolutePath(
            string Root,
            PathSegment[] Segments,
            bool IgnoreCase)
        {
            public bool TryMakeRelativeTo(ParsedAbsolutePath workspace, out PathSegment[] relativePath)
            {
                relativePath = [];
                if (IgnoreCase != workspace.IgnoreCase)
                    return false;

                var comparison = IgnoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!string.Equals(Root, workspace.Root, comparison)
                    || Segments.Length < workspace.Segments.Length)
                {
                    return false;
                }

                for (var index = 0; index < workspace.Segments.Length; index++)
                {
                    if (!string.Equals(Segments[index].Text, workspace.Segments[index].Text, comparison))
                        return false;
                }

                relativePath = Segments[workspace.Segments.Length..];
                return true;
            }
        }

        [GeneratedRegex(
            """(?<![A-Za-z0-9_:/\\])(?<path>(?:[A-Za-z]:[\\/]|\\\\|//|/(?!/))[^\"\r\n]*?)(?<suffix>\(\d+,\d+(?:,\d+,\d+)?\))(?=:\s|\s|$)""",
            RegexOptions.CultureInvariant)]
        private static partial Regex CompilerLocationPathRegex();

        [GeneratedRegex(
            """(?<![A-Za-z0-9_:/\\])(?<path>(?:[A-Za-z]:[\\/]|\\\\|//|/(?!/))[^\"\r\n]*?)(?<suffix>:line[ \t]+\d+)""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex StackTracePathRegex();

        [GeneratedRegex(
            """(?<prefix>\")(?<path>(?:[A-Za-z]:[\\/]|\\\\|//|/(?!/))[^\"\r\n]+)(?<suffix>\")""",
            RegexOptions.CultureInvariant)]
        private static partial Regex DoubleQuotedPathRegex();

        [GeneratedRegex(
            """(?<prefix>')(?<path>(?:[A-Za-z]:[\\/]|\\\\|//|/(?!/))[^'\r\n]+)(?<suffix>')""",
            RegexOptions.CultureInvariant)]
        private static partial Regex SingleQuotedPathRegex();

        [GeneratedRegex(
            """(?<![A-Za-z0-9_:/\\])(?<path>(?:[A-Za-z]:[\\/]|\\\\|//|/(?!/))[^ \t\r\n\"'<>|]+)""",
            RegexOptions.CultureInvariant)]
        private static partial Regex BarePathRegex();
    }
}

/// <summary>
/// Stable, secret-safe wire representation of an external command execution.
/// </summary>
internal sealed record PublicCommandExecutionDto(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string TargetPath,
    int ExitCode,
    bool Succeeded,
    long DurationMs,
    string StdOut,
    string StdErr,
    string? EarlyKillReason);

/// <summary>
/// Stable test-run wire representation used both by <c>test_run</c> and by validation DTOs that
/// embed a <see cref="TestRunResultDto"/>.
/// </summary>
internal sealed record PublicTestRunResultDto(
    PublicCommandExecutionDto Execution,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestFailureDto> Failures,
    TestRunFailureEnvelopeDto? FailureEnvelope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CommandDurationMs = null);

/// <summary>
/// Applies <see cref="TestRunPublicProjection"/> whenever a sibling host response serializes an
/// embedded <see cref="TestRunResultDto"/> through <see cref="JsonDefaults.Indented"/>.
/// </summary>
internal sealed class TestRunResultDtoJsonConverter : JsonConverter<TestRunResultDto>
{
    public override TestRunResultDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("The public test-run converter supports serialization only.");

    public override void Write(
        Utf8JsonWriter writer,
        TestRunResultDto value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, TestRunPublicProjection.Create(value), options);
}

/// <summary>
/// Applies the execution projection to sibling validation responses such as
/// <c>build_workspace</c> and <c>build_project</c> that embed the shared
/// <see cref="CommandExecutionDto"/> directly.
/// </summary>
internal sealed class CommandExecutionDtoJsonConverter : JsonConverter<CommandExecutionDto>
{
    public override CommandExecutionDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("The public command-execution converter supports serialization only.");

    public override void Write(
        Utf8JsonWriter writer,
        CommandExecutionDto value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, TestRunPublicProjection.CreateExecution(value), options);
}
