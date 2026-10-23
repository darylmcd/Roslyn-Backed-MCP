using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Preserves captured MSBuild globals while keeping their values inside command execution.</summary>
internal static class MsBuildCommandProperties
{
    internal static List<string> Append(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> properties)
    {
        var result = arguments.ToList();
        foreach (var (name, value) in properties)
        {
            // An explicit nullable per-project package-root snapshot takes precedence over globals.
            if (name.Equals("RestorePackagesPath", StringComparison.OrdinalIgnoreCase)
                && arguments.Contains("--packages")) continue;
            result.Add("-p:" + name + "=" + Escape(Decode(value)));
        }
        return result;
    }

    // The MSBuild API takes escaped globals. Decode once before encoding the argv
    // transport, so nested escapes and literal separator characters keep their meaning.
    private static string Decode(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && index + 2 < value.Length && byte.TryParse(value.AsSpan(index + 1, 2),
                System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var decoded))
            {
                result.Append((char)decoded);
                index += 2;
            }
            else result.Append(value[index]);
        }
        return result.ToString();
    }

    private static string Escape(string value)
    {
        foreach (var character in new[] { '%', ';', ',', '$', '@', '(', ')', '\'', '"', '*', '?' })
            value = value.Replace(character.ToString(), "%" + ((int)character).ToString("X2"), StringComparison.Ordinal);
        return value;
    }

    internal static string Redact(string text, IReadOnlyList<string> arguments) => new Redactor(arguments).Text(text);
    internal static CommandExecutionDto Redact(CommandExecutionDto execution, IReadOnlyList<string> arguments) => new Redactor(arguments).Execution(execution);
    internal static TestRunResultDto Redact(TestRunResultDto result, IReadOnlyList<string> arguments) => new Redactor(arguments).Result(result);

    internal sealed class Redactor
    {
        private static readonly HashSet<string> AuthoredDataKeys = new(StringComparer.Ordinal)
        {
            "MetadataReferenceCounts", "MissingMetadataFileNames", "ExtraMetadataFileNames", "MissingAnalyzerFileNames",
            "ExtraAnalyzerFileNames", "SdkGeneratedConfig", "SdkGeneratedConfigPath", "DifferentLineIndices",
            "GeneratedConfigDifferenceKinds", "ExecutionEntryCounts", "ExecutionNodeCounts", "ExecutionIdentityMismatchComponents",
            "ExecutionContextMismatchComponents", "LayoutMapFailureKind", "LayoutMapDirectorySuffix",
            "RestoreArtifactFailureKind", "RestoreNativeContextCount", "RestorePackageExpectationCount", "RestoreAssetsExist",
            "RestoreCompileTargetPresent", "RestoreRuntimeTargetPresent", OwnedResourceCleanup.OutcomeDataKey,
            "BuildGraphFailureTypes", "BuildGraphErrorCodes", "BuildGraphFailureStages", "BuildGraphXmlFailureSymbols", "CompilerReferenceLineage",
            InstalledPackageAuthority.DependencyGrantRequiredKey
        };
        private readonly Regex? _pattern;
        internal bool HasValues => _pattern is not null;
        internal Redactor(IReadOnlyList<string> arguments)
        {
            var values = new List<string>();
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                var prefixes = new[] { "-p:", "/p:", "--property:", "-property:", "/property:" };
                var prefix = prefixes.FirstOrDefault(value => argument.StartsWith(value, StringComparison.OrdinalIgnoreCase));
                string? assignments = prefix is not null ? argument[prefix.Length..] : null;
                if (assignments is null && new[] { "-p", "/p", "--property", "-property", "/property" }.Contains(argument, StringComparer.OrdinalIgnoreCase)
                    && index + 1 < arguments.Count) assignments = arguments[++index];
                if (assignments is null) continue;
                foreach (var assignment in Assignments(assignments))
                {
                    var separator = assignment.IndexOf('=');
                    if (separator < 0) continue;
                    var value = assignment[(separator + 1)..].Trim('"');
                    if (value.Length == 0) continue;
                    values.Add(value);
                    values.Add(Decode(value));
                }
            }
            if (values.Count > 0)
                _pattern = new Regex(string.Join("|", values.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(value => value.Length).Select(Regex.Escape)), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        private static IEnumerable<string> Assignments(string text)
        {
            var start = 0;
            var quoted = false;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '"') quoted = !quoted;
                else if (!quoted && text[index] is ';' or ',')
                {
                    yield return text[start..index];
                    start = index + 1;
                }
            }
            yield return text[start..];
        }
        internal string Text(string text)
        {
            try { return _pattern?.Replace(text, "[redacted]") ?? text; }
            catch (RegexMatchTimeoutException) { return "[redacted]"; }
        }
        internal Exception Failure(Exception exception)
        {
            if (!HasValues || CanPreserve(exception, new HashSet<Exception>(ReferenceEqualityComparer.Instance))) return exception;
            Exception sanitized = exception switch
            {
                TaskCanceledException canceled => new TaskCanceledException(Text(canceled.Message), null, canceled.CancellationToken),
                OperationCanceledException canceled => new OperationCanceledException(Text(canceled.Message), canceled.CancellationToken),
                AggregateException aggregate => new AggregateException(Text(aggregate.Message), aggregate.InnerExceptions.Select(Failure)),
                PhysicalGraphMismatchException => new PhysicalGraphMismatchException(Text(exception.Message)),
                WorkspaceMutationOwnershipException => new WorkspaceMutationOwnershipException(Text(exception.Message)),
                RoslynMcp.Core.Services.PublicInvalidOperationException => new RoslynMcp.Core.Services.PublicInvalidOperationException(Text(exception.Message)),
                RoslynMcp.Core.Services.PublicArgumentException argument when RoslynMcp.Core.Services.WorkspacePathPolicyRefusal.IsDenied(argument)
                    => new RoslynMcp.Core.Services.PublicArgumentException(Text(argument.PublicMessage), Text(argument.ParamName ?? "paths")),
                ArgumentException argument when RoslynMcp.Core.Services.WorkspacePathPolicyRefusal.IsDenied(argument)
                    => new ArgumentException(Text(argument.Message), argument.ParamName is null ? null : Text(argument.ParamName)),
                UnauthorizedAccessException => new UnauthorizedAccessException(Text(exception.Message)),
                TimeoutException => new TimeoutException(Text(exception.Message)),
                IOException => new IOException(Text(exception.Message)),
                NotSupportedException => new NotSupportedException(Text(exception.Message)),
                _ => RoslynMcp.Core.Services.InvalidOperationErrors.Internal("Command execution or result processing failed (" + Text(exception.GetType().Name) + ").")
            };
            try
            {
                foreach (System.Collections.DictionaryEntry entry in exception.Data)
                {
                    if (entry.Key is not string key) continue;
                    sanitized.Data[AuthoredDataKeys.Contains(key) ? key : Text(key)] = entry.Value switch
                    {
                        string value => Text(value),
                        string[] values when key == OwnedResourceCleanup.OutcomeDataKey => values.Take(OwnedResourceCleanup.MaximumOutcomeCount)
                            .Select(value => BoundOutcome(value is null ? "[unavailable]" : Text(value))).ToArray(),
                        null => null,
                        bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => entry.Value,
                        _ => "[unavailable]"
                    };
                }
            }
            catch { sanitized.Data["DiagnosticData"] = "[unavailable]"; }
            RoslynMcp.Core.Services.WorkspacePathPolicyRefusal.Transfer(exception, sanitized);
            return sanitized;
        }
        private bool CanPreserve(Exception exception, HashSet<Exception> visited)
        {
            if (!visited.Add(exception) || visited.Count > 32) return false;
            try
            {
                var rendered = exception.ToString();
                if (Text(rendered) != rendered) return false;
                foreach (System.Collections.DictionaryEntry entry in exception.Data)
                {
                    if (entry.Key is not string key || (!AuthoredDataKeys.Contains(key) && Text(key) != key)) return false;
                    if (entry.Value is string value) { if (Text(value) != value) return false; }
                    else if (key == OwnedResourceCleanup.OutcomeDataKey && entry.Value is string[] outcomes)
                    {
                        if (outcomes.Length > OwnedResourceCleanup.MaximumOutcomeCount || outcomes.Any(outcome => outcome is null
                            || outcome.Length > OwnedResourceCleanup.MaximumOutcomeLength || Text(outcome) != outcome)) return false;
                    }
                    else if (entry.Value is not (null or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)) return false;
                }
                return exception is AggregateException aggregate
                    ? aggregate.InnerExceptions.All(inner => CanPreserve(inner, visited))
                    : exception.InnerException is null || CanPreserve(exception.InnerException, visited);
            }
            catch { return false; } // Opaque custom diagnostic state cannot be proven safe to retain.
        }
        private static string BoundOutcome(string value) => value.Length > OwnedResourceCleanup.MaximumOutcomeLength
            ? value[..OwnedResourceCleanup.MaximumOutcomeLength] : value;
        private string? Optional(string? value) => value is null ? null : Text(value);
        internal CommandExecutionDto Execution(CommandExecutionDto execution) => execution with
        {
            Command = Text(execution.Command),
            Arguments = execution.Arguments.Select(Text).ToArray(),
            WorkingDirectory = Text(execution.WorkingDirectory),
            TargetPath = Text(execution.TargetPath),
            StdOut = Text(execution.StdOut),
            StdErr = Text(execution.StdErr),
            EarlyKillReason = Optional(execution.EarlyKillReason)
        };
        internal DiagnosticDto Diagnostic(DiagnosticDto diagnostic) => diagnostic with
        {
            Message = Text(diagnostic.Message),
            FilePath = Optional(diagnostic.FilePath),
            Location = diagnostic.Location is not { } location ? null : location with
            {
                FilePath = Text(location.FilePath),
                ContainingMember = Optional(location.ContainingMember),
                PreviewText = Optional(location.PreviewText)
            }
        };
        internal TestRunResultDto Result(TestRunResultDto result) => result with
        {
            Execution = Execution(result.Execution),
            Failures = result.Failures.Select(failure => failure with
            {
                DisplayName = Text(failure.DisplayName),
                FullyQualifiedName = Optional(failure.FullyQualifiedName),
                Message = Text(failure.Message),
                StackTrace = Optional(failure.StackTrace)
            }).ToArray(),
            FailureEnvelope = result.FailureEnvelope is not { } envelope ? null : envelope with
            {
                Summary = Text(envelope.Summary),
                StdOutTail = Optional(envelope.StdOutTail),
                StdErrTail = Optional(envelope.StdErrTail)
            }
        };
    }
}
