using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Captures compiler inputs or preparation writer facts through one owned child/channel; native output and arguments remain private.</summary>
internal static class NativeCompilerTaskInputs
{
    internal sealed record Reference(string Path, string OriginalPath, string SourceProject, ImmutableArray<string> Aliases, bool EmbedInteropTypes,
        MetadataImageKind Kind)
    {
        public override string ToString() => "CompilerReferenceInput";
    }

    internal sealed record Capture(ImmutableArray<Reference> References, ImmutableArray<string> Arguments, string Output,
        CompilerArgumentParseReceipt Parsing)
    {
        public override string ToString() => $"CompilerTaskInputs {{ ReferenceCount = {References.Length}, ArgumentCount = {Arguments.Length} }}";
    }
    internal enum CapturePurpose { FinalCompiler, PreparationOutputs, RestoreSettings }
    internal sealed record RawCapture(ImmutableArray<Reference> References, ImmutableArray<string> Arguments, string Output,
        ImmutableArray<NativeOutputProducerObserver.Receipt> Writers, bool CompilerIdentityUnavailable,
        ImmutableArray<NativeRestoreSettingsObserver.Receipt> RestoreSettings)
    {
        public override string ToString() => "NativeCompilerRawReceipt";
    }

    internal static async Task<Capture> ReadAsync(Project project, IReadOnlyDictionary<string, string> capturedGlobals,
        string? targetFramework, string toolsVersion, ImmutableArray<string> compileTargets, CompilerArgumentInputContext parsing, CancellationToken ct)
    {
        var (capture, _) = await ReadRawAsync(project.FilePath ?? throw Refusal("The native compiler owner has no physical identity."), capturedGlobals, targetFramework, toolsVersion, compileTargets, CapturePurpose.FinalCompiler, ct).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(project.FilePath) ?? throw Refusal("The native compiler owner has no physical directory.");
        try
        {
            var configurations = await parsing.CaptureInputsAsync(project.AnalyzerConfigDocuments.Select(document => document.FilePath
                ?? throw Refusal("A native analyzer configuration has no physical identity.")), CompilerArgumentInputRole.AnalyzerConfig, ct).ConfigureAwait(false);
            var parsed = (await parsing.ParseAsync(capture.Arguments, ct).ConfigureAwait(false)).WithInputs(configurations);
            await parsing.ValidateAnalyzerConfigurationAsync(project, ct).ConfigureAwait(false);
            await parsing.AssertCurrentAsync(parsed, ct).ConfigureAwait(false);
            ValidateOptionsAndInputs(project, capture, parsed);
            var nativeProjects = ImmutableArray.CreateBuilder<CompilerArgumentReference>();
            foreach (var edge in project.ProjectReferences)
            {
                var target = project.Solution.GetProject(edge.ProjectId)
                    ?? throw Refusal("The native compiler edge has no independently loaded target.");
                var matches = capture.References.Where(input => Matches(input, target, edge.Aliases, directory)).ToArray();
                if (matches.Length != 1)
                    throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.References, [parsed]);
                var input = matches[0];
                nativeProjects.Add(new(Path.GetFullPath(input.Path, directory),
                    new MetadataReferenceProperties(input.Kind, input.Aliases, input.EmbedInteropTypes),
                    CompilerArgumentReferenceKind.NativeProject));
            }
            var resolved = await parsing.ResolveReferencesAsync(parsed, nativeProjects.ToImmutable(), ct).ConfigureAwait(false);
            var actualReferences = ReconcileReferenceOccurrences(capture.References, resolved.References, directory);
            return new(actualReferences, capture.Arguments, capture.Output, resolved.Parsing);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PublicInvalidOperationException) { throw; }
        catch (NativeSemanticContextMismatchException) { throw; }
        catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure))
        {
            throw new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], capturedGlobals)).Failure(failure);
        }
        catch (Exception failure) { throw Safe(failure); }
    }

    internal static ImmutableArray<Reference> ReconcileReferenceOccurrences(ImmutableArray<Reference> producer,
        ImmutableArray<CompilerArgumentReference> resolved, string directory)
    {
        var remainingAliases = producer.Select(input => input.Aliases.DefaultIfEmpty("global").ToList()).ToArray();
        var emitted = new HashSet<int>();
        var actualReferences = ImmutableArray.CreateBuilder<Reference>();
        foreach (var reference in resolved)
        {
            var requested = reference.Properties.Aliases.DefaultIfEmpty("global").ToArray();
            var matches = Enumerable.Range(0, producer.Length).Where(index =>
                SameReferenceOperand(producer[index].Path, producer[index].EmbedInteropTypes, producer[index].Kind,
                    reference.Path, reference.Properties.EmbedInteropTypes, reference.Properties.Kind, directory)
                && ContainsAliasOccurrences(remainingAliases[index], requested)).ToArray();
            if (matches.Length > 1) throw Refusal("The compiler producer repeated an indistinguishable reference input.");
            if (reference.Kind == CompilerArgumentReferenceKind.NativeProject && matches.Length != 1)
                throw Refusal("A native compiler project reference lost its actual producer identity.");
            if (matches.Length == 1)
            {
                foreach (var alias in requested) remainingAliases[matches[0]].Remove(alias);
                if (emitted.Add(matches[0])) actualReferences.Add(producer[matches[0]]);
            }
            else actualReferences.Add(new(reference.Path, "", "", reference.Properties.Aliases,
                reference.Properties.EmbedInteropTypes, reference.Properties.Kind));
        }
        if (remainingAliases.Any(aliases => aliases.Count != 0)) throw Refusal("The compiler argument output omitted a producer reference occurrence.");
        return actualReferences.ToImmutable();
    }

    internal static Task<(RawCapture Capture, bool ProducerSucceeded)> PrepareOutputsAsync(string path,
        IReadOnlyDictionary<string, string> capturedGlobals, string? targetFramework, string toolsVersion, ImmutableArray<string> compileTargets, CancellationToken ct)
        => ReadRawAsync(path, capturedGlobals, targetFramework, toolsVersion, compileTargets, CapturePurpose.PreparationOutputs, ct);

    internal static async Task<(RawCapture Capture, bool ProducerSucceeded)> ReadRawAsync(string path, IReadOnlyDictionary<string, string> capturedGlobals,
        string? targetFramework, string toolsVersion, ImmutableArray<string> compileTargets, CapturePurpose purpose, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path) ?? throw Refusal("The native compiler owner has no physical directory.");
        var globals = purpose == CapturePurpose.RestoreSettings
            ? new Dictionary<string, string>(capturedGlobals, StringComparer.OrdinalIgnoreCase)
            : EffectiveGlobals(capturedGlobals, targetFramework);
        var expectedContext = NativeTaskExecutionIdentity.EscapedContextIdentity(globals, toolsVersion, ct);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var name = "roslyn-mcp-compiler-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // XMake sets BuildParameters.LogTaskInputs from GLOBAL verbosity before
        // running targets. IEventSource3 alone does not set this execution flag.
        var args = MsBuildCommandProperties.Append(["msbuild", path, "-nologo", "-m:1", "-nr:false", "-noconsolelogger", "-verbosity:diagnostic", "-toolsVersion:" + toolsVersion,
            "-target:" + string.Join(";", compileTargets), "-logger:" + typeof(CompilerReferenceCaptureLogger).FullName + ","
            + typeof(CompilerReferenceCaptureLogger).Assembly.Location + ";" + name], globals);
        var reading = ReceiveAsync(pipe, path, expectedContext, purpose, lifetime.Token);
        // stdout/stderr are redirected and drained by the owned runner. No native output is
        // returned, logged or included in any failure; even the retained tail is one character.
        var running = new DotnetCommandRunner(outputLimit: 1).RunAsync(directory, path, args, lifetime.Token);
        try
        {
            var first = await Task.WhenAny(reading, running).ConfigureAwait(false);
            if (first == running && !pipe.IsConnected) lifetime.Cancel();
            if (first == reading && !reading.IsCompletedSuccessfully) await reading.ConfigureAwait(false);
            var execution = await running.ConfigureAwait(false);
            if (purpose != CapturePurpose.PreparationOutputs && !execution.Succeeded) throw Refusal("The native input producer did not complete successfully.");
            var capture = await reading.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (purpose == CapturePurpose.FinalCompiler && capture.CompilerIdentityUnavailable) throw new NativeTaskExecutionUnavailableException();
            return (capture, execution.Succeeded);
        }
        catch (Exception failure)
        {
            lifetime.Cancel();
            // Observe both owned operations before releasing their channel/lifetime.
            var secondary = new List<Exception>();
            await ObserveAsync(running).ConfigureAwait(false);
            await ObserveAsync(reading).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                var cancelled = failure is OperationCanceledException existing && existing.CancellationToken == ct
                    ? existing : new OperationCanceledException(ct);
                OwnedResourceCleanup.AttachOutcomes(cancelled, secondary.Select(other => "CompilerCapture:" + other.GetType().Name).ToArray());
                throw cancelled;
            }
            if (secondary.Count != 0) throw new AggregateException("Native compiler input capture and cleanup failed.",
                new[] { Safe(failure) }.Concat(secondary));
            if (failure is PublicInvalidOperationException or NativeTaskExecutionUnavailableException) throw;
            throw Safe(failure);

            async Task ObserveAsync(Task operation)
            {
                try { await operation.ConfigureAwait(false); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { /* The owned child/channel was intentionally cancelled. */ }
                catch (Exception other) when (!ReferenceEquals(other, failure)) { secondary.Add(Safe(other)); }
                catch (Exception same) when (ReferenceEquals(same, failure)) { /* The primary failure is already propagated. */ }
            }
        }
    }

    internal static Dictionary<string, string> EffectiveGlobals(IReadOnlyDictionary<string, string> capturedGlobals, string? targetFramework)
    {
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DesignTimeBuild"] = "True",
            ["NonExistentFile"] = "__NonExistentSubDir__\\__NonExistentFile__",
            ["BuildProjectReferences"] = "False",
            ["BuildingProject"] = "False",
            ["ProvideCommandLineArgs"] = "True",
            ["SkipCompilerExecution"] = "True",
            ["ContinueOnError"] = "ErrorAndContinue",
            ["ShouldUnsetParentConfigurationAndPlatform"] = "False"
        };
        foreach (var pair in capturedGlobals) globals[pair.Key] = pair.Value;
        // The selected framework is an evaluated value; all globals handed to
        // MSBuild and the context boundary must use its escaped API representation.
        if (!string.IsNullOrEmpty(targetFramework)) globals["TargetFramework"] = Microsoft.Build.Evaluation.ProjectCollection.Escape(targetFramework);
        return globals;
    }

    private static async Task<RawCapture> ReceiveAsync(NamedPipeServerStream pipe, string projectPath, string expectedContext, CapturePurpose purpose, CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
        var tasks = new Dictionary<string, TaskCapture>(StringComparer.Ordinal);
        var writers = ImmutableArray.CreateBuilder<NativeOutputProducerObserver.Receipt>();
        var restoreSettings = ImmutableArray.CreateBuilder<NativeRestoreSettingsObserver.Receipt>();
        var settingsTasks = new HashSet<string>(StringComparer.Ordinal);
        var unavailableCompilers = new HashSet<string>(StringComparer.Ordinal);
        TaskCapture? current = null;
        var budget = new CompilerReferenceCaptureBudget(ct);
        while (true)
        {
            var lengthBytes = new byte[sizeof(int)];
            await pipe.ReadExactlyAsync(lengthBytes, ct).ConfigureAwait(false);
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            budget.AcceptWireLength(length);
            var bytes = new byte[length];
            await pipe.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            var frame = CompilerReferenceCaptureFrame.Decode(bytes, ct, out var logicalSize)
                ?? throw Refusal("The compiler input channel returned an invalid frame.");
            budget.AcceptLogicalLength(logicalSize);
            if (frame.Fields is null || frame.Fields.Any(value => value is null) || frame.Context is null || frame.Kind is null)
                throw Refusal("The compiler input channel returned an incomplete frame.");
            switch (frame.Kind)
            {
                case "RestoreSettings" when frame.Fields.Length >= 4:
                    if (!settingsTasks.Add(frame.Context)) throw Refusal("The native settings producer repeated a task.");
                    var parameters = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    for (var index = 4; index < frame.Fields.Length;)
                    {
                        if (index + 2 > frame.Fields.Length) throw Refusal("The native settings producer returned an incomplete parameter.");
                        var parameter = frame.Fields[index++];
                        var size = Count(frame.Fields[index++]);
                        if (size > frame.Fields.Length - index || !parameters.TryAdd(parameter, frame.Fields.Skip(index).Take(size).ToArray()))
                            throw Refusal("The native settings producer returned an invalid parameter.");
                        index += size;
                    }
                    restoreSettings.Add(new(frame.Fields[0], frame.Fields[1], frame.Fields[2], frame.Fields[3], parameters));
                    break;
                case "CompilerUnavailable" when frame.Fields.Length == 2 && frame.Context.Length > 0:
                    if (!Path.IsPathFullyQualified(frame.Fields[0]) || tasks.ContainsKey(frame.Context) || !unavailableCompilers.Add(frame.Context))
                        throw Refusal("The compiler producer returned an invalid unavailable task identity.");
                    break;
                case "OutputWriter" when frame.Fields.Length == 9:
                    if (!FileSystemPath.Comparer.Equals(Path.GetFullPath(frame.Fields[0]), Path.GetFullPath(projectPath)))
                        throw Refusal("A native writer receipt belongs to a different project context.");
                    writers.Add(new(frame.Fields[0], frame.Fields[1], frame.Fields[2], frame.Fields[3], frame.Fields[4], frame.Fields[5], frame.Fields[6], frame.Fields[7],
                        frame.Fields[8].Length == 0 ? null : frame.Fields[8]));
                    break;
                case "Task" or "References" or "Reference" or "Arguments" or "Argument" or "Output" or "Finished"
                    when purpose != CapturePurpose.FinalCompiler:
                    break;
                case "Completed" when purpose != CapturePurpose.FinalCompiler && frame.Fields.Length == 0 && frame.Context.Length == 0:
                    return new([], [], "", writers.ToImmutable(), unavailableCompilers.Count != 0, restoreSettings.ToImmutable());
                case "Task" when frame.Fields.Length == 4 && current is null:
                    if (unavailableCompilers.Contains(frame.Context)) throw Refusal("The compiler producer repeated an unavailable task identity.");
                    if (!FileSystemPath.Comparer.Equals(Path.GetFullPath(frame.Fields[0]), Path.GetFullPath(projectPath))
                        || !FileSystemPath.Comparer.Equals(Path.GetFullPath(frame.Fields[1]), TrustedTaskAssembly())
                        || frame.Fields[2] != expectedContext || frame.Fields[3] != "Microsoft.CodeAnalysis.BuildTasks.Csc")
                        throw Refusal("The compiler producer did not identify the trusted SDK task and owner.");
                    current = new TaskCapture();
                    if (!tasks.TryAdd(frame.Context, current)) throw Refusal("The compiler producer repeated a task context.");
                    break;
                case "References" when frame.Fields.Length == 1 && tasks.TryGetValue(frame.Context, out current):
                    if (current.Finished || current.ExpectedReferences is not null) throw Refusal("The compiler producer repeated its reference parameter.");
                    current.ExpectedReferences = Count(frame.Fields[0]);
                    break;
                case "Reference" when frame.Fields.Length == 5 && tasks.TryGetValue(frame.Context, out current):
                    if (current.Finished || current.ExpectedReferences is not int count || current.References.Count >= count)
                        throw Refusal("The compiler producer returned an unordered reference parameter.");
                    var embed = frame.Fields[4];
                    if (embed.Length != 0 && !bool.TryParse(embed, out _)) throw Refusal("The compiler producer returned invalid interop metadata.");
                    current.References.Add(new(frame.Fields[0], frame.Fields[1], frame.Fields[2],
                        frame.Fields[3].Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray(),
                        string.Equals(embed, "true", StringComparison.OrdinalIgnoreCase), MetadataImageKind.Assembly));
                    break;
                case "Arguments" when frame.Fields.Length == 1 && tasks.TryGetValue(frame.Context, out current):
                    if (current.Finished || current.ExpectedArguments is not null) throw Refusal("The compiler producer repeated its argument output.");
                    current.ExpectedArguments = Count(frame.Fields[0]);
                    break;
                case "Argument" when frame.Fields.Length == 1 && tasks.TryGetValue(frame.Context, out current):
                    if (current.Finished || current.ExpectedArguments is not int argumentCount || current.Arguments.Count >= argumentCount)
                        throw Refusal("The compiler producer returned an unordered argument output.");
                    current.Arguments.Add(frame.Fields[0]);
                    break;
                case "Output" when frame.Fields.Length == 1 && tasks.TryGetValue(frame.Context, out current):
                    if (current.Finished || current.Output is not null) throw Refusal("The compiler producer repeated its output identity.");
                    current.Output = frame.Fields[0];
                    break;
                case "Finished" when frame.Fields.Length == 1 && tasks.TryGetValue(frame.Context, out current):
                    if (frame.Fields[0] != "true" || current.Finished) throw Refusal("The compiler producer task failed or repeated completion.");
                    current.Finished = true;
                    current = null;
                    break;
                case "Completed" when frame.Fields.Length == 0 && frame.Context.Length == 0 && current is null:
                    if (unavailableCompilers.Count != 0) return new([], [], "", writers.ToImmutable(), true, restoreSettings.ToImmutable());
                    if (tasks.Count != 1) throw Refusal("The compiler producer did not establish exactly one task invocation.");
                    var result = tasks.Values.Single();
                    if (!result.Finished || result.ExpectedReferences is null || result.ExpectedArguments is null || result.Output is null
                        || result.References.Count != result.ExpectedReferences || result.Arguments.Count != result.ExpectedArguments)
                        throw Refusal("The compiler producer returned an incomplete task receipt (Finished=" + result.Finished
                            + ";References=" + result.References.Count + "/" + result.ExpectedReferences
                            + ";Arguments=" + result.Arguments.Count + "/" + result.ExpectedArguments + ";Output=" + (result.Output is not null) + ").");
                    return new(result.References.ToImmutableArray(), result.Arguments.ToImmutableArray(), result.Output, writers.ToImmutable(), false, restoreSettings.ToImmutable());
                default: throw Refusal("The compiler input channel returned an unexpected frame.");
            }
        }
    }

    private static int Count(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
        && count is >= 0 and <= 100_000 ? count : throw Refusal("The compiler input channel returned an invalid item count.");

    internal static string RegisteredSdkRoot() => Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)
        ?? throw Refusal("The registered SDK has no physical identity.");
    private static string TrustedTaskAssembly() => Path.GetFullPath(Path.Combine(RegisteredSdkRoot(), "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"));

    private static void ValidateOptionsAndInputs(Project project, RawCapture capture, CompilerArgumentParseReceipt parsing)
    {
        var parsed = parsing.Arguments;
        var path = project.FilePath ?? throw Refusal("The compiler owner has no physical path.");
        var directory = Path.GetDirectoryName(path) ?? throw Refusal("The compiler owner has no physical directory.");
        var parse = parsed.ParseOptions.DocumentationMode == DocumentationMode.None
            ? parsed.ParseOptions.WithDocumentationMode(DocumentationMode.Parse) : parsed.ParseOptions;
        var native = project.CompilationOptions ?? throw Refusal("The native compiler owner has no compilation options.");
        var options = parsed.CompilationOptions.WithXmlReferenceResolver(new XmlFileResolver(directory))
            .WithSourceReferenceResolver(new SourceFileResolver([], directory))
            .WithMetadataReferenceResolver(native.MetadataReferenceResolver)
            // ProjectState installs this native provider after command-line parsing.
            // Its exact config paths/text were approved and byte-bound above.
            .WithSyntaxTreeOptionsProvider(native.SyntaxTreeOptionsProvider)
            .WithStrongNameProvider(new DesktopStrongNameProvider(parsed.KeyFileSearchPaths, Path.GetTempPath()))
            .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);
        if (!Equals(project.ParseOptions, parse) || !Equals(native, options) || parsed.CompilationName != project.AssemblyName)
            throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.Options, [parsing]);
        if (!FileSystemPath.Comparer.Equals(Path.GetFullPath(capture.Output, directory),
                Path.GetFullPath(Path.Combine(parsed.OutputDirectory, parsed.OutputFileName
                    ?? throw Refusal("The compiler producer has no output identity.")))))
            throw Refusal("The compiler producer output differs from its actual argument output.");
        if (project.CompilationOutputInfo.AssemblyPath is not { } nativeOutput
            || !FileSystemPath.Comparer.Equals(Path.GetFullPath(nativeOutput, directory), Path.GetFullPath(capture.Output, directory)))
            throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.Output, [parsing]);
        EqualPaths(project.Documents.Select(document => document.FilePath), parsed.SourceFiles.Select(source => source.Path));
        EqualPaths(project.AdditionalDocuments.Select(document => document.FilePath), parsed.AdditionalFiles.Select(source => source.Path));
        EqualPaths(project.AnalyzerConfigDocuments.Select(document => document.FilePath), parsed.AnalyzerConfigPaths);
        EqualPaths(project.AnalyzerReferences.Select(reference => reference switch
        {
            AnalyzerReference file when AnalyzerReferenceIsolation.GetFileReference(file) is not null => file.FullPath,
            UnresolvedAnalyzerReference unresolved => unresolved.FullPath,
            _ => throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.Inputs, [parsing])
        }), parsed.AnalyzerReferences.Select(reference => reference.FilePath));
        // The SDK task's References input covers task-generated switches. References
        // inside response files are additional real compiler inputs, independently
        // approved/resolved by the receipt and checked against the native graph.
        AssertReferenceArguments(capture.References, CompilerArgumentInputContext.TaskReferenceArguments(capture.Arguments, directory), directory);

        void EqualPaths(IEnumerable<string?> nativePaths, IEnumerable<string?> actualPaths)
        {
            string Canonical(string? value) => value is not null ? Path.GetFullPath(value, directory)
                : throw Refusal("A compiler input has no physical identity.");
            if (!nativePaths.Select(Canonical).OrderBy(value => value, FileSystemPath.Comparer)
                    .SequenceEqual(actualPaths.Select(Canonical).OrderBy(value => value, FileSystemPath.Comparer), FileSystemPath.Comparer))
                throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.Inputs, [parsing]);
        }
    }

    internal static void AssertReferenceArguments(IEnumerable<Reference> references, IEnumerable<CommandLineReference> arguments, string directory)
    {
        // Csc emits one switch per alias. Compare every occurrence without changing
        // the full alias identity owned by the native task item.
        var parsed = arguments.SelectMany(reference => reference.Properties.Aliases.DefaultIfEmpty("global").Select(alias =>
            ReferenceKey(reference.Reference, [alias], reference.Properties.EmbedInteropTypes, reference.Properties.Kind, directory)))
            .OrderBy(value => value, ReferenceIdentity.Comparer);
        var task = references.SelectMany(reference => reference.Aliases.DefaultIfEmpty("global").Select(alias =>
            ReferenceKey(reference.Path, [alias], reference.EmbedInteropTypes, reference.Kind, directory)))
            .OrderBy(value => value, ReferenceIdentity.Comparer);
        if (!parsed.SequenceEqual(task, ReferenceIdentity.Comparer))
            throw Refusal("The compiler producer reference inputs differ from its actual argument output.");
    }

    internal static bool SameReferenceOperand(string firstPath, bool firstEmbed, MetadataImageKind firstKind,
        string secondPath, bool secondEmbed, MetadataImageKind secondKind, string directory)
        => FileSystemPath.Comparer.Equals(Path.GetFullPath(firstPath, directory), Path.GetFullPath(secondPath, directory))
            && firstEmbed == secondEmbed && firstKind == secondKind;

    internal static bool ContainsAliasOccurrences(IEnumerable<string> available, IEnumerable<string> requested)
    {
        var remaining = available.ToList();
        return requested.All(alias => remaining.Remove(alias));
    }

    internal static ReferenceIdentity ReferenceKey(string path, IEnumerable<string> aliases, bool embed, MetadataImageKind kind, string directory) =>
        new(Path.GetFullPath(path, directory), aliases.DefaultIfEmpty("global").OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(), embed, kind);

    internal sealed record ReferenceIdentity(string Path, ImmutableArray<string> Aliases, bool Embed, MetadataImageKind Kind)
    {
        internal static IdentityComparer Comparer { get; } = new();
        public bool Equals(ReferenceIdentity? other) => Comparer.Equals(this, other);
        public override int GetHashCode() => Comparer.GetHashCode(this);
        public override string ToString() => "CompilerReferenceIdentity";
        internal sealed class IdentityComparer : IComparer<ReferenceIdentity>, IEqualityComparer<ReferenceIdentity>
        {
            public int Compare(ReferenceIdentity? left, ReferenceIdentity? right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left is null) return -1;
                if (right is null) return 1;
                var path = FileSystemPath.Comparer.Compare(left.Path, right.Path);
                if (path != 0) return path;
                for (var index = 0; index < Math.Min(left.Aliases.Length, right.Aliases.Length); index++)
                {
                    var alias = StringComparer.Ordinal.Compare(left.Aliases[index], right.Aliases[index]);
                    if (alias != 0) return alias;
                }
                var length = left.Aliases.Length.CompareTo(right.Aliases.Length);
                if (length != 0) return length;
                var embed = left.Embed.CompareTo(right.Embed);
                return embed != 0 ? embed : left.Kind.CompareTo(right.Kind);
            }
            public bool Equals(ReferenceIdentity? left, ReferenceIdentity? right) => Compare(left, right) == 0;
            public int GetHashCode(ReferenceIdentity value)
            {
                var hash = new HashCode();
                hash.Add(value.Path, FileSystemPath.Comparer);
                foreach (var alias in value.Aliases) hash.Add(alias, StringComparer.Ordinal);
                hash.Add(value.Embed); hash.Add(value.Kind);
                return hash.ToHashCode();
            }
        }
    }

    internal static bool Matches(Reference input, Project target, ImmutableArray<string> aliases, string directory)
    {
        if (input.Kind != MetadataImageKind.Assembly) return false;
        var output = Path.GetFullPath(input.OriginalPath.Length == 0 ? input.Path : input.OriginalPath, directory);
        if (!(target.OutputFilePath is { } targetOutput && FileSystemPath.Comparer.Equals(output, Path.GetFullPath(targetOutput))
            || target.OutputRefFilePath is { } referenceOutput && FileSystemPath.Comparer.Equals(output, Path.GetFullPath(referenceOutput)))) return false;
        if (input.SourceProject.Length != 0 && (target.FilePath is null
            || !FileSystemPath.Comparer.Equals(Path.GetFullPath(input.SourceProject, directory), Path.GetFullPath(target.FilePath)))) return false;
        return input.Aliases.DefaultIfEmpty("global").OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(aliases.DefaultIfEmpty("global").OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private sealed class TaskCapture
    {
        internal int? ExpectedReferences { get; set; }
        internal int? ExpectedArguments { get; set; }
        internal string? Output { get; set; }
        internal bool Finished { get; set; }
        internal List<Reference> References { get; } = [];
        internal List<string> Arguments { get; } = [];
    }

    private static PublicInvalidOperationException Refusal(string message) => new(message);
    private static Exception Safe(Exception failure) => failure is PublicInvalidOperationException or NativeTaskExecutionUnavailableException ? failure
        : Refusal("The native compiler input protocol failed (" + failure.GetType().Name + ").");
}
