using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;

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
                StdOutTail = redactor.RedactOptionalText(result.FailureEnvelope.StdOutTail),
                StdErrTail = redactor.RedactOptionalText(result.FailureEnvelope.StdErrTail),
            };
        var failures = result.Failures
            .Select(failure => failure with
            {
                Message = redactor.RedactText(failure.Message),
                StackTrace = redactor.RedactOptionalText(failure.StackTrace),
            })
            .ToArray();

        return new PublicTestRunResultDto(
            redactor.CreatePublicExecution(),
            result.Total,
            result.Passed,
            result.Failed,
            result.Skipped,
            failures,
            failureEnvelope);
    }

    private sealed class ExecutionRedactor
    {
        private readonly CommandExecutionDto _execution;
        private readonly List<(string Value, string Replacement)> _replacements = [];
        private readonly string[] _publicArguments;
        private readonly string _publicWorkingDirectory;
        private readonly string _publicTargetPath;

        public ExecutionRedactor(CommandExecutionDto execution)
        {
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
            _publicTargetPath = ProjectTargetPath(execution.TargetPath);
            _publicWorkingDirectory = IsFullyQualifiedPath(execution.WorkingDirectory)
                ? "."
                : execution.WorkingDirectory;
            _publicArguments = ProjectArguments(execution.Arguments);

            AddPathReplacement(execution.TargetPath, _publicTargetPath);
            AddPathReplacement(execution.WorkingDirectory, _publicWorkingDirectory);
            _replacements.Sort(static (left, right) => right.Value.Length.CompareTo(left.Value.Length));
        }

        public PublicCommandExecutionDto CreatePublicExecution() =>
            new(
                _execution.Command,
                _publicArguments,
                _publicWorkingDirectory,
                _publicTargetPath,
                _execution.ExitCode,
                _execution.Succeeded,
                _execution.DurationMs,
                RedactText(_execution.StdOut),
                RedactText(_execution.StdErr),
                RedactOptionalText(_execution.EarlyKillReason));

        public string RedactText(string value)
        {
            var redacted = DiagnosticPathProjector.Project(value, _execution.WorkingDirectory);
            foreach (var (sensitiveValue, replacement) in _replacements)
            {
                redacted = redacted.Replace(
                    sensitiveValue,
                    replacement,
                    StringComparison.OrdinalIgnoreCase);
            }

            return redacted;
        }

        public string? RedactOptionalText(string? value) =>
            value is null ? null : RedactText(value);

        private string[] ProjectArguments(IReadOnlyList<string> arguments)
        {
            var projected = arguments.ToArray();
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (IsOption(argument, "--filter") && index + 1 < arguments.Count)
                {
                    var filter = arguments[++index];
                    projected[index] = RedactedValue;
                    AddLiteralReplacement(
                        filter,
                        ProjectSensitiveTextValue(filter),
                        minimumLength: 1);
                    continue;
                }

                if (TryGetOptionValue(argument, "--filter", out var inlineFilter))
                {
                    projected[index] = argument[..(argument.IndexOf('=') + 1)] + RedactedValue;
                    AddLiteralReplacement(
                        inlineFilter,
                        ProjectSensitiveTextValue(inlineFilter),
                        minimumLength: 1);
                    continue;
                }

                if (IsOption(argument, "--results-directory") && index + 1 < arguments.Count)
                {
                    var resultsDirectory = arguments[++index];
                    projected[index] = RedactedResultsDirectory;
                    AddPathReplacement(resultsDirectory, RedactedResultsDirectory);
                    continue;
                }

                if (TryGetOptionValue(argument, "--results-directory", out var inlineResultsDirectory))
                {
                    projected[index] = argument[..(argument.IndexOf('=') + 1)] + RedactedResultsDirectory;
                    AddPathReplacement(inlineResultsDirectory, RedactedResultsDirectory);
                    continue;
                }

                if (PathsEqual(argument, _execution.TargetPath))
                {
                    projected[index] = _publicTargetPath;
                    continue;
                }

                if (IsFullyQualifiedPath(argument))
                {
                    projected[index] = "<path>";
                    AddPathReplacement(argument, projected[index]);
                }
            }

            return projected;
        }

        private void AddPathReplacement(string value, string replacement)
        {
            if (!IsFullyQualifiedPath(value))
                return;

            AddLiteralReplacement(value, replacement, minimumLength: 2);
            AddLiteralReplacement(value.Replace('\\', '/'), replacement, minimumLength: 2);
            AddLiteralReplacement(value.Replace('/', '\\'), replacement, minimumLength: 2);
        }

        private void AddLiteralReplacement(string value, string replacement, int minimumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length < minimumLength || value == replacement)
                return;

            if (_replacements.Any(existing =>
                    string.Equals(existing.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _replacements.Add((value, replacement));
        }

        private static bool IsOption(string argument, string option) =>
            string.Equals(argument, option, StringComparison.OrdinalIgnoreCase);

        private static string ProjectSensitiveTextValue(string value) =>
            value.Length < RedactedValue.Length
                ? new string('*', value.Length)
                : RedactedValue;

        private static bool TryGetOptionValue(string argument, string option, out string value)
        {
            var prefix = option + "=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = argument[prefix.Length..];
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(
                left.Replace('\\', '/'),
                right.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

        private static string ProjectTargetPath(string targetPath)
        {
            if (!IsFullyQualifiedPath(targetPath))
                return targetPath;

            var normalized = targetPath.TrimEnd('/', '\\').Replace('\\', '/');
            var separator = normalized.LastIndexOf('/');
            return separator >= 0 && separator + 1 < normalized.Length
                ? normalized[(separator + 1)..]
                : "<target>";
        }

        private static bool IsFullyQualifiedPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (Path.IsPathFullyQualified(value))
                return true;

            return value[0] == '/'
                || (value.Length >= 3
                    && char.IsAsciiLetter(value[0])
                    && value[1] == ':'
                    && value[2] is '/' or '\\');
        }
    }

    /// <summary>
    /// Projects filesystem-shaped text without relying on the current host operating system.
    /// Child output can contain Windows, UNC, and POSIX paths regardless of where the server runs.
    /// </summary>
    private static partial class DiagnosticPathProjector
    {
        public static string Project(string value, string workspaceRoot)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var projected = ProjectMatches(CompilerLocationPathRegex(), value, workspaceRoot);
            projected = ProjectMatches(StackTracePathRegex(), projected, workspaceRoot);
            projected = ProjectMatches(DoubleQuotedPathRegex(), projected, workspaceRoot);
            projected = ProjectMatches(SingleQuotedPathRegex(), projected, workspaceRoot);
            return ProjectMatches(BarePathRegex(), projected, workspaceRoot);
        }

        private static string ProjectMatches(Regex regex, string value, string workspaceRoot) =>
            regex.Replace(value, match => ProjectMatch(match, workspaceRoot));

        private static string ProjectMatch(Match match, string workspaceRoot)
        {
            var group = match.Groups["path"];
            var path = group.Value;
            var pathLength = path.Length;
            while (pathLength > 0 && IsTrailingPunctuation(path[pathLength - 1]))
                pathLength--;

            var candidate = path[..pathLength].TrimEnd();
            if (!TryParseAbsolutePath(candidate, out var parsedPath))
                return match.Value;

            var replacement = TryParseAbsolutePath(workspaceRoot, out var parsedWorkspace)
                && parsedPath.TryMakeRelativeTo(parsedWorkspace, out var relativePath)
                    ? relativePath
                    : RedactedExternalPath;
            var pathOffset = group.Index - match.Index;
            return string.Concat(
                match.Value.AsSpan(0, pathOffset),
                replacement,
                match.Value.AsSpan(pathOffset + candidate.Length));
        }

        private static bool TryParseAbsolutePath(string value, out ParsedAbsolutePath path)
        {
            path = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Replace('\\', '/');
            string root;
            string remainder;
            bool ignoreCase;

            if (normalized.Length >= 3
                && char.IsAsciiLetter(normalized[0])
                && normalized[1] == ':'
                && normalized[2] == '/')
            {
                root = normalized[..3];
                remainder = normalized[3..];
                ignoreCase = true;
            }
            else if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                var rootSegments = normalized[2..]
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (rootSegments.Length < 2)
                    return false;

                root = $"//{rootSegments[0]}/{rootSegments[1]}";
                remainder = string.Join('/', rootSegments.Skip(2));
                ignoreCase = true;
            }
            else if (normalized[0] == '/')
            {
                root = "/";
                remainder = normalized[1..];
                ignoreCase = false;
            }
            else
            {
                return false;
            }

            var segments = new List<string>();
            foreach (var segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                    continue;
                if (segment == "..")
                {
                    if (segments.Count == 0)
                        return false;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            path = new ParsedAbsolutePath(root, segments.ToArray(), ignoreCase);
            return true;
        }

        private static bool IsTrailingPunctuation(char value) =>
            value is '.' or ',' or ';' or ':' or ')' or ']' or '}';

        private readonly record struct ParsedAbsolutePath(
            string Root,
            string[] Segments,
            bool IgnoreCase)
        {
            public bool TryMakeRelativeTo(ParsedAbsolutePath workspace, out string relativePath)
            {
                relativePath = string.Empty;
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
                    if (!string.Equals(Segments[index], workspace.Segments[index], comparison))
                        return false;
                }

                relativePath = Segments.Length == workspace.Segments.Length
                    ? "."
                    : string.Join('/', Segments.Skip(workspace.Segments.Length));
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
    TestRunFailureEnvelopeDto? FailureEnvelope);

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
