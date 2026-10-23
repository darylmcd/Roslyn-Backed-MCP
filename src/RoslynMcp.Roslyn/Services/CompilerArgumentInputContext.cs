using System.Collections.Immutable;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Authorizes and freezes every filesystem dependency consumed by the public compiler parser.</summary>
internal sealed class CompilerArgumentInputContext(string projectDirectory,
    Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
    Func<string, CancellationToken, Task<byte[]?>> readBytes,
    Func<string, CancellationToken, Task<FileContentDigest?>> readDigest,
    Func<string> stagePathFactory,
    Func<string, CancellationToken, Task> assertNativeStageDisjoint)
{
    private readonly OwnedPhysicalRetainedValidation? _ownedPhysicalValidation;

    private CompilerArgumentInputContext(string projectDirectory,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
        Func<string, CancellationToken, Task<byte[]?>> readBytes,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest,
        Func<string> stagePathFactory,
        Func<string, CancellationToken, Task> assertNativeStageDisjoint,
        OwnedPhysicalRetainedValidation retainedValidation)
        : this(projectDirectory, authorizePaths, readBytes, readDigest, stagePathFactory, assertNativeStageDisjoint)
        => _ownedPhysicalValidation = retainedValidation;

    // Only the product's certificate-bound physical reader composition uses this
    // factory. The ordinary constructor preserves arbitrary supplied-reader behavior.
    internal static CompilerArgumentInputContext CreateOwnedPhysical(string projectDirectory,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
        Func<string, CancellationToken, Task<byte[]?>> readBytes,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest,
        Func<string> stagePathFactory,
        Func<string, CancellationToken, Task> assertNativeStageDisjoint)
        => new(projectDirectory, authorizePaths, readBytes, readDigest, stagePathFactory,
            assertNativeStageDisjoint, new OwnedPhysicalRetainedValidation(authorizePaths));

    private sealed class OwnedPhysicalRetainedValidation(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths)
    {
        internal async Task AssertAsync(ImmutableArray<PhysicalInputIdentity> inputs,
            ImmutableArray<CompilerArgumentDirectory> directories, ImmutableArray<CompilerRulesetExpansion> expansions,
            CancellationToken ct)
        {
            // Preserve preexisting expansion-refusal precedence; repeat after the grant below.
            CompilerArgumentParseReceipt.AssertExpansions(expansions, ct);
            // Policy denial is never translated into a physical-read failure.
            await authorizePaths(CompilerArgumentParseReceipt.GrantPaths(inputs, directories), ct).ConfigureAwait(false);
            try
            {
                await CompilerArgumentParseReceipt.AssertPhysicalInputsAsync(inputs, directories, expansions,
                    ImmutableHashSet<string>.Empty, ct).ConfigureAwait(false);
            }
            catch (FileNotFoundException) { throw Refusal("Physical compiler argument inputs changed after preparation; recreate the preview."); }
            catch (DirectoryNotFoundException) { throw Refusal("Physical compiler argument inputs changed after preparation; recreate the preview."); }
            catch (IOException failure) { throw Refusal("A compiler argument input could not be read (" + failure.GetType().Name + ")."); }
            catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("A compiler argument input could not be read under current authority."); }
        }
    }

    private readonly string _directory = projectDirectory;
    private readonly Func<string> _stagePathFactory = stagePathFactory;
    private readonly Func<string, CancellationToken, Task> _assertNativeStageDisjoint = assertNativeStageDisjoint;

    internal static string CreateStagePath() => Path.Combine(Path.GetTempPath(), "roslynmcp-compiler-inputs-" + Guid.NewGuid().ToString("N"));
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _authorize = authorizePaths;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _read = readBytes;
    private readonly Func<string, CancellationToken, Task<FileContentDigest?>> _readDigest = readDigest;

    private Task<byte[]?> ReadAuthorizedAsync(string path, CancellationToken ct) => ReadAuthorizedAsync(path, _read, ct);
    private Task<FileContentDigest?> ReadDigestAuthorizedAsync(string path, CancellationToken ct) => ReadAuthorizedAsync(path, _readDigest, ct);
    private async Task<T?> ReadAuthorizedAsync<T>(string path, Func<string, CancellationToken, Task<T?>> read, CancellationToken ct) where T : class
    {
        await _authorize([path], ct).ConfigureAwait(false);
        WorkspaceManager.EnsurePhysicalPath(path);
        try { return await read(path, ct).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (IOException failure) { throw Refusal("A compiler argument input could not be read (" + failure.GetType().Name + ")."); }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("A compiler argument input could not be read under current authority."); }
    }

    internal async Task<ImmutableArray<CompilerArgumentInputObservation>> CaptureInputsAsync(IEnumerable<string> paths,
        CompilerArgumentInputRole role, CancellationToken ct)
    {
        CompilerArgumentInputObservation.RequireRole(role);
        var result = ImmutableArray.CreateBuilder<CompilerArgumentInputObservation>();
        foreach (var path in paths.Distinct(FileSystemPath.Comparer))
        {
            var digest = await ReadDigestAuthorizedAsync(path, ct).ConfigureAwait(false);
            result.Add(new(Identity(path, digest), role));
        }
        return result.ToImmutable();
    }

    internal async Task ValidateAnalyzerConfigurationAsync(Project project, CancellationToken ct)
    {
        foreach (var document in project.AnalyzerConfigDocuments)
        {
            var path = document.FilePath ?? throw Refusal("A native analyzer configuration has no physical identity.");
            var bytes = await ReadAuthorizedAsync(path, ct).ConfigureAwait(false) ?? throw Refusal("A native analyzer configuration is missing.");
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var physicalText = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var nativeText = await document.GetTextAsync(ct).ConfigureAwait(false);
            if (!nativeText.ContentEquals(Microsoft.CodeAnalysis.Text.SourceText.From(physicalText)))
                throw Refusal("Native analyzer configuration text differs from the current compiler input epoch.");
        }
    }

    internal Task<CompilerArgumentParseReceipt> ParseAsync(ImmutableArray<string> arguments, CancellationToken ct)
        => new Session(this).ParseAsync(arguments, ct);

    internal Task<CompilerArgumentReferenceReceipt> ResolveReferencesAsync(CompilerArgumentParseReceipt receipt,
        ImmutableArray<CompilerArgumentReference> nativeProjectReferences, CancellationToken ct)
        => new Session(this).ResolveReferencesAsync(receipt, nativeProjectReferences, ct);

    internal Task AssertCurrentAsync(CompilerArgumentParseReceipt receipt, CancellationToken ct)
        => _ownedPhysicalValidation is null
            ? receipt.AssertCurrentAsync(_authorize, ReadDigestAuthorizedAsync, ct)
            : _ownedPhysicalValidation.AssertAsync(receipt.Inputs, receipt.Directories, receipt.Expansions, ct);

    private Task AssertRetainedInputsAsync(ImmutableArray<PhysicalInputIdentity> inputs,
        ImmutableArray<CompilerArgumentDirectory> directories, ImmutableArray<CompilerRulesetExpansion> expansions,
        CancellationToken ct)
        => _ownedPhysicalValidation is null
            ? CompilerArgumentParseReceipt.AssertInputsAsync(inputs, directories, expansions, _authorize, ReadDigestAuthorizedAsync, ct)
            : _ownedPhysicalValidation.AssertAsync(inputs, directories, expansions, ct);

    internal static ImmutableArray<CommandLineReference> TaskReferenceArguments(IEnumerable<string> arguments, string directory)
    {
        // Only these individual switches are parsed here. No response, ruleset,
        // directory or wildcard switch can reach this pure grammar-only parse.
        return CSharpCommandLineParser.Default.Parse(arguments.Where(argument => TryOption(argument, out var name, out _)
            && (name.Equals("r", StringComparison.OrdinalIgnoreCase) || name.Equals("reference", StringComparison.OrdinalIgnoreCase)
                || name.Equals("l", StringComparison.OrdinalIgnoreCase) || name.Equals("link", StringComparison.OrdinalIgnoreCase))),
            directory, sdkDirectory: null).MetadataReferences;
    }

    private sealed class FrozenReferenceResolver(ImmutableArray<string> searchPaths, string directory,
        IReadOnlyDictionary<string, PhysicalInputIdentity> observations) : SourceFileResolver(searchPaths, directory)
    {
        internal HashSet<string> Pending { get; } = new(FileSystemPath.Comparer);
        protected override bool FileExists([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? resolvedPath)
        {
            if (resolvedPath is null) return false;
            var canonical = Path.GetFullPath(resolvedPath);
            if (observations.TryGetValue(canonical, out var known)) return known.Kind == PhysicalInputKind.File;
            Pending.Add(canonical);
            // Stop the official search at its first unobserved candidate. This
            // provisional result is never consumed; the async caller records it
            // and repeats the same protocol with the actual existence result.
            return true;
        }
    }

    private sealed class Session
    {
        private readonly CompilerArgumentInputContext _owner;
        private readonly string _stage;
        internal Session(CompilerArgumentInputContext owner)
        {
            _owner = owner;
            _stage = Path.GetFullPath(owner._stagePathFactory());
        }
        private readonly List<PhysicalInputIdentity> _inputs = [];
        private readonly List<CompilerArgumentInputObservation> _observations = [];

        private void Observe(PhysicalInputIdentity input, CompilerArgumentInputRole role)
        {
            _inputs.Add(input);
            _observations.Add(new(input, role));
        }
        private readonly List<CompilerArgumentDirectory> _directories = [];
        private readonly List<CompilerRulesetExpansion> _expansions = [];
        private readonly HashSet<string> _stagedPaths = new(FileSystemPath.Comparer);
        private int _ruleRoots;
        private readonly Dictionary<string, byte[]> _stagedFiles = new(FileSystemPath.Comparer);
        private readonly Dictionary<string, PhysicalInputKind> _stagedDirectories = new(FileSystemPath.Comparer);
        private PhysicalDirectoryScope.OwnedRoot? _stageOwner;

        internal async Task<CompilerArgumentParseReceipt> ParseAsync(ImmutableArray<string> arguments, CancellationToken ct)
        {
            Exception? primary = null;
            CompilerArgumentParseReceipt? completed = null;
            try
            {
                var flattened = new List<string>();
                var active = new HashSet<string>(FileSystemPath.Comparer);
                foreach (var argument in arguments) await ExpandResponseAsync(argument, active, flattened, ct).ConfigureAwait(false);
                var safeArguments = new List<string>();
                foreach (var argument in flattened)
                {
                    ct.ThrowIfCancellationRequested();
                    if (TryOption(argument, out var name, out var value))
                    {
                        if (name.Equals("ruleset", StringComparison.OrdinalIgnoreCase) && value is { Length: > 0 })
                        {
                            safeArguments.Add("-ruleset:" + QuotePath(await StageRuleAsync(Path.GetFullPath(Decode(value), _owner._directory), ct).ConfigureAwait(false)));
                            continue;
                        }
                        if (name.Equals("recurse", StringComparison.OrdinalIgnoreCase) && value is { Length: > 0 })
                        {
                            safeArguments.AddRange(await RewritePatternAsync(Decode(value), recursive: true, prefix: "", emptyPrefix: "-recurse:", CompilerArgumentInputRole.SourceMatch, ct).ConfigureAwait(false));
                            continue;
                        }
                        if ((name.Equals("additionalfile", StringComparison.OrdinalIgnoreCase) || name.Equals("analyzerconfig", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("embed", StringComparison.OrdinalIgnoreCase)) && value is { Length: > 0 })
                        {
                            var role = name.Equals("additionalfile", StringComparison.OrdinalIgnoreCase) ? CompilerArgumentInputRole.AdditionalMatch
                                : name.Equals("analyzerconfig", StringComparison.OrdinalIgnoreCase) ? CompilerArgumentInputRole.AnalyzerConfig
                                : CompilerArgumentInputRole.EmbeddedMatch;
                            foreach (var path in SeparatedPaths(value, _owner._directory))
                                safeArguments.AddRange(await RewritePatternAsync(path, recursive: false, prefix: "-" + name + ":", emptyPrefix: "-" + name + ":", role, ct).ConfigureAwait(false));
                            continue;
                        }
                        if ((name.Equals("lib", StringComparison.OrdinalIgnoreCase) || name.Equals("libpath", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("libpaths", StringComparison.OrdinalIgnoreCase)) && value is { Length: > 0 })
                            foreach (var path in SeparatedPaths(value, _owner._directory))
                                _directories.Add(await CompilerArgumentDirectory.CaptureAsync(Path.GetFullPath(path, _owner._directory),
                                    pattern: null, recursive: false, _owner._authorize, ct).ConfigureAwait(false));
                        safeArguments.Add(argument);
                    }
                    else if (Decode(argument).IndexOfAny(['*', '?']) >= 0)
                    {
                        safeArguments.AddRange(await RewritePatternAsync(Decode(argument), recursive: false, prefix: "", emptyPrefix: "", CompilerArgumentInputRole.SourceMatch, ct).ConfigureAwait(false));
                    }
                    else safeArguments.Add(argument);
                }
                // A retained POSIX stage must remain outside every recursive source
                // traversal. Prove physical disjointness before creating any stage entry.
                await _owner.AssertRetainedInputsAsync(PhysicalInputIdentity.MergeConsistent(_inputs), _directories.ToImmutableArray(), _expansions.ToImmutableArray(), ct).ConfigureAwait(false);
                await CreateStageAsync(ct).ConfigureAwait(false);
                await AssertStageAsync(ct).ConfigureAwait(false);
                var parsed = CSharpCommandLineParser.Default.Parse(safeArguments, _owner._directory, RuntimeEnvironment.GetRuntimeDirectory());
                _ = PhysicalInputIdentity.MergeConsistent(_inputs);
                await AssertStageAsync(ct).ConfigureAwait(false);
                completed = new(parsed, _observations, _directories.ToImmutableArray(), _expansions.ToImmutableArray());
                return completed;
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                if (_stageOwner is not null)
                {
                    try { CleanupStage(); }
                    catch (Exception cleanup) when (primary is not null)
                    {
                        var outcomes = cleanup.Data[OwnedResourceCleanup.OutcomeDataKey] as string[] ?? [];
                        OwnedResourceCleanup.AttachOutcomes(primary, new[] { "CompilerParserStage:" + cleanup.GetType().Name }.Concat(outcomes).ToArray());
                    }
                    catch (Exception cleanup) { throw Refusal("Compiler parser staging cleanup failed (" + cleanup.GetType().Name + ")."); }
                }
                if (primary is null && completed is not null) await _owner.AssertCurrentAsync(completed, ct).ConfigureAwait(false);
            }
        }

        private async Task<byte[]?> ReadAsync(string path, CompilerArgumentInputRole role, CancellationToken ct)
        {
            var bytes = await _owner.ReadAuthorizedAsync(path, ct).ConfigureAwait(false);
            Observe(Identity(path, FileContentDigest.FromBytes(bytes)), role);
            return bytes;
        }

        private async Task<PhysicalInputIdentity> ReadDigestAsync(string path, CompilerArgumentInputRole role, CancellationToken ct)
        {
            var identity = Identity(path, await _owner.ReadDigestAuthorizedAsync(path, ct).ConfigureAwait(false));
            Observe(identity, role);
            return identity;
        }

        private async Task CreateStageAsync(CancellationToken ct)
        {
            if (_stagedPaths.Count == 0) return;
            await _owner._assertNativeStageDisjoint(_stage, ct).ConfigureAwait(false);
            var physicalStage = PhysicalPathResolver.Resolve(_stage);
            foreach (var traversal in _directories.Where(directory => directory.Recursive))
            {
                var source = PhysicalPathResolver.Resolve(traversal.Path);
                if (FileSystemPath.Comparer.Equals(source, physicalStage)
                    || FileSystemPath.IsStrictDescendant(source, physicalStage)
                    || FileSystemPath.IsStrictDescendant(physicalStage, source))
                    throw Refusal("Compiler parser staging requires a temporary directory outside every recursive source traversal.");
            }
            ct.ThrowIfCancellationRequested();
            _stageOwner = PhysicalDirectoryScope.CreateOwnedRoot(_stage);
            foreach (var (path, kind) in _stagedDirectories)
                if (kind == PhysicalInputKind.Directory)
                    _stageOwner.EnsureDirectories(Path.GetRelativePath(_stage, path), ct);
            foreach (var (path, bytes) in _stagedFiles)
                await _stageOwner.WriteFileAsync(Path.GetRelativePath(_stage, path), bytes, ct).ConfigureAwait(false);
        }

        private async Task AssertStageAsync(CancellationToken ct)
        {
            if (_stageOwner is null) return;
            // Revalidate caller-owned dependencies before inspecting the private stage.
            // No source policy grant authorizes parser-stage mutation or cleanup.
            await _owner._authorize(_inputs.Select(input => input.FilePath).Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
            try
            {
                _stageOwner.AssertCurrentPath(ct);
                var expected = new Dictionary<string, PhysicalDirectoryEntryKind>(FileSystemPath.Comparer);
                foreach (var path in _stagedFiles.Keys)
                    expected.Add(Path.GetRelativePath(_stage, path), PhysicalDirectoryEntryKind.File);
                foreach (var (path, kind) in _stagedDirectories)
                    if (kind == PhysicalInputKind.Directory)
                        expected.Add(Path.GetRelativePath(_stage, path), PhysicalDirectoryEntryKind.Directory);
                var actualEntries = _stageOwner.EnumerateRelativeEntries(ct);
                if (expected.Count != actualEntries.Count || actualEntries.Any(entry =>
                    !expected.TryGetValue(entry.RelativePath, out var kind) || kind != entry.Kind))
                    throw Refusal("The compiler parser stage contains an unowned input; recreate the preview.");
                foreach (var (path, bytes) in _stagedFiles)
                {
                    var actual = await _stageOwner.ReadFileIfPresentAsync(Path.GetRelativePath(_stage, path), ct).ConfigureAwait(false);
                    if (actual is null || !actual.AsSpan().SequenceEqual(bytes))
                        throw Refusal("A compiler parser input changed; recreate the preview.");
                }
                _stageOwner.AssertCurrentPath(ct);
            }
            catch (IOException failure) { throw Refusal("The owned compiler parser stage changed (" + failure.GetType().Name + "); recreate the preview."); }
        }

        private void CleanupStage()
        {
            if (_stageOwner is null) return;
            Exception? primary = null;
            try
            {
                // The exact admitted empty namespace owns subsequent contents. Its
                // held capability cannot delete a replacement root or ancestor.
                _ = _stageOwner.Delete(CancellationToken.None);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Compiler parser stage release failed.", primary, _stageOwner); }
        }

        internal async Task<CompilerArgumentReferenceReceipt> ResolveReferencesAsync(CompilerArgumentParseReceipt receipt,
            ImmutableArray<CompilerArgumentReference> nativeProjectReferences, CancellationToken ct)
        {
            await _owner.AssertCurrentAsync(receipt, ct).ConfigureAwait(false);
            var parsed = receipt.Arguments;
            // Both Roslyn's compiler RelativePathResolver and public SourceFileResolver
            // invoke FileUtilities.ResolveRelativePath with this same search protocol.
            // The override performs no IO: unknown probes are approved and frozen
            // asynchronously before the official resolver is run again.
            var observations = new Dictionary<string, PhysicalInputIdentity>(FileSystemPath.Comparer);
            var resolver = new FrozenReferenceResolver(parsed.ReferencePaths, _owner._directory, observations);
            var result = ImmutableArray.CreateBuilder<CompilerArgumentReference>();
            var nativeAliases = nativeProjectReferences.Select(reference => reference.Properties.Aliases.DefaultIfEmpty("global").ToList()).ToArray();
            var emittedNative = new HashSet<int>();
            foreach (var reference in parsed.MetadataReferences)
            {
                // A native CompilationReference does not require a built PE. Only
                // independently matched absolute task operands can use that role.
                var requestedAliases = reference.Properties.Aliases.DefaultIfEmpty("global").ToArray();
                var native = Path.IsPathFullyQualified(reference.Reference)
                    ? Enumerable.Range(0, nativeProjectReferences.Length).Where(index =>
                        nativeProjectReferences[index].Kind == CompilerArgumentReferenceKind.NativeProject
                        && NativeCompilerTaskInputs.SameReferenceOperand(nativeProjectReferences[index].Path,
                            nativeProjectReferences[index].Properties.EmbedInteropTypes, nativeProjectReferences[index].Properties.Kind,
                            reference.Reference, reference.Properties.EmbedInteropTypes, reference.Properties.Kind, _owner._directory)
                        && NativeCompilerTaskInputs.ContainsAliasOccurrences(nativeAliases[index], requestedAliases)).ToArray() : [];
                if (native.Length > 1) throw Refusal("A compiler project reference has ambiguous native ownership.");
                if (native.Length == 1)
                {
                    var index = native[0];
                    foreach (var alias in requestedAliases) nativeAliases[index].Remove(alias);
                    // Preserve current authorization of the matched coordinate without consuming an output PE.
                    await _owner._authorize([nativeProjectReferences[index].Path], ct).ConfigureAwait(false);
                    if (emittedNative.Add(index)) result.Add(nativeProjectReferences[index]);
                    continue;
                }
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    resolver.Pending.Clear();
                    var resolved = resolver.ResolveReference(reference.Reference, baseFilePath: null);
                    if (resolver.Pending.Count == 0)
                    {
                        if (resolved is null) throw Refusal("An actual compiler metadata reference cannot be resolved under its frozen search context.");
                        result.Add(new(resolved, reference.Properties, CompilerArgumentReferenceKind.MetadataFile));
                        break;
                    }
                    foreach (var candidate in resolver.Pending)
                        await ObserveAsync(candidate).ConfigureAwait(false);
                }
            }
            if (nativeAliases.Any(aliases => aliases.Count != 0))
                throw Refusal("The compiler argument output omitted a native project reference alias occurrence.");
            var complete = receipt.WithInputs(_observations);
            await _owner.AssertCurrentAsync(complete, ct).ConfigureAwait(false);
            return new(result.ToImmutable(), complete);

            async Task ObserveAsync(string candidate)
            {
                if (observations.ContainsKey(candidate)) return;
                await _owner._authorize([candidate], ct).ConfigureAwait(false);
                PhysicalInputIdentity identity;
                try
                {
                    identity = CompilerArgumentDirectory.ProbePhysicalKind(candidate, ct) switch
                    {
                        PhysicalInputKind.Directory => new(candidate, true, []),
                        PhysicalInputKind.Missing => new(candidate, false, []),
                        _ => await ReadDigestAsync(candidate, CompilerArgumentInputRole.MetadataProbe, ct).ConfigureAwait(false)
                    };
                }
                catch (FileNotFoundException) { identity = new(candidate, false, []); }
                catch (DirectoryNotFoundException) { identity = new(candidate, false, []); }
                catch (IOException failure) { throw Refusal("A compiler reference input could not be observed (" + failure.GetType().Name + ")."); }
                catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("A compiler reference input could not be observed under current authority."); }
                observations.Add(candidate, identity);
                Observe(identity, CompilerArgumentInputRole.MetadataProbe);
            }
        }

        private async Task ExpandResponseAsync(string argument, HashSet<string> active, List<string> flattened, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            argument = argument.TrimEnd();
            if (!argument.StartsWith('@')) { flattened.Add(argument); return; }
            var path = Path.GetFullPath(Decode(argument[1..]).TrimEnd(), _owner._directory);
            if (!active.Add(path)) throw Refusal("Compiler response inputs contain a recursive include cycle.");
            try
            {
                var bytes = await ReadAsync(path, CompilerArgumentInputRole.Response, ct).ConfigureAwait(false) ?? throw Refusal("A required compiler response input is missing.");
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                    foreach (var nested in CommandLineParser.SplitCommandLineIntoArguments(line, removeHashComments: true))
                        if (!nested.Equals("/noconfig", StringComparison.OrdinalIgnoreCase) && !nested.Equals("-noconfig", StringComparison.OrdinalIgnoreCase))
                            await ExpandResponseAsync(nested, active, flattened, ct).ConfigureAwait(false);
                        else
                        {
                            // Keep Roslyn's own nested-response warning rather than
                            // deleting the switch or reconstructing its diagnostic.
                            var warningInput = Path.Combine(_stage, "noconfig-" + _stagedFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".rsp");
                            _stagedPaths.Add(warningInput);
                            _stagedFiles.Add(warningInput, Encoding.UTF8.GetBytes(nested));
                            flattened.Add("@" + QuotePath(warningInput));
                        }
            }
            finally { active.Remove(path); }
        }

        private async Task<string[]> RewritePatternAsync(string path, bool recursive, string prefix, string emptyPrefix,
            CompilerArgumentInputRole role, CancellationToken ct)
        {
            var full = Path.GetFullPath(path, _owner._directory);
            if (!recursive && full.IndexOfAny(['*', '?']) < 0) return [prefix + QuotePath(full)];
            var directory = Path.GetFullPath(Path.GetDirectoryName(path) is { Length: > 0 } declaredDirectory ? declaredDirectory : ".", _owner._directory);
            var pattern = Path.GetFileName(path);
            var enumeration = await CompilerArgumentDirectory.CaptureAsync(directory, pattern.Trim(['\t', '\n', '\v', '\f', '\r', ' ', '\x0085', '\x00A0']), recursive, _owner._authorize, ct).ConfigureAwait(false);
            _directories.Add(enumeration);
            foreach (var match in enumeration.Matches) _ = await ReadDigestAsync(match, role, ct).ConfigureAwait(false);
            if (!enumeration.Matches.IsEmpty) return enumeration.Matches.Select(match => prefix + QuotePath(match)).ToArray();
            // Retain the official no-match diagnostics (including /recurse's
            // deliberately silent empty-directory behavior) without letting it
            // enumerate an unfrozen source tree a second time.
            var empty = Path.Combine(_stage, "empty-" + _stagedDirectories.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _stagedPaths.Add(empty);
            _stagedDirectories.Add(empty, enumeration.Kind);
            if (enumeration.Kind == PhysicalInputKind.File) _stagedFiles.Add(empty, []);
            return [emptyPrefix + QuotePath(Path.Combine(empty, pattern))];
        }

        private async Task<string> StageRuleAsync(string path, CancellationToken ct)
        {
            var states = new Dictionary<string, RuleStage>(FileSystemPath.Comparer);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var caseVariants = new Dictionary<string, int>(StringComparer.Ordinal);
            var rootOrdinal = _ruleRoots++;
            var root = await LoadAsync(path).ConfigureAwait(false);
            var result = await VisitAsync(root, isRoot: true).ConfigureAwait(false);
            foreach (var state in states.Values)
                if (state.Bytes is not null)
                    _stagedFiles[state.Destination] = state.Document is null ? state.Bytes
                        : Encoding.UTF8.GetBytes(state.Document.ToString(SaveOptions.DisableFormatting));
            return result;

            async Task<RuleStage> LoadAsync(string source)
            {
                if (states.TryGetValue(source, out var known)) return known;
                // Official ruleset recursion compares FilePath.ToLowerInvariant even on
                // Linux. Case-distinct inputs therefore need case-distinct stage names
                // whose lowercased names are identical, retaining native cycle identity.
                var lower = source.ToLowerInvariant();
                caseVariants.TryGetValue(lower, out var variant);
                caseVariants[lower] = checked(variant + 1);
                var caseSuffix = new string(Enumerable.Range(0, 32).Select(bit => ((variant >> bit) & 1) == 0 ? 'a' : 'A').ToArray());
                var name = rootOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-"
                    + NativeIdentityEncoding.StringHash(lower, ct) + "-" + caseSuffix + ".ruleset";
                var destination = Path.Combine(_stage, name);
                _stagedPaths.Add(destination);
                await _owner._authorize([source], ct).ConfigureAwait(false);
                try
                {
                    if (CompilerArgumentDirectory.ProbePhysicalKind(source, ct) == PhysicalInputKind.Directory)
                    {
                        Observe(new(source, true, []), CompilerArgumentInputRole.Ruleset);
                        var directory = new RuleStage(source, destination, null, null, []);
                        states.Add(source, directory);
                        return directory;
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException failure) { throw Refusal("A compiler ruleset input could not be observed (" + failure.GetType().Name + ")."); }
                catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("A compiler ruleset input could not be observed under current authority."); }
                var bytes = await ReadAsync(source, CompilerArgumentInputRole.Ruleset, ct).ConfigureAwait(false);
                XDocument? document = null;
                if (bytes is not null)
                {
                    try
                    {
                        using var stream = new MemoryStream(bytes, writable: false);
                        using var reader = XmlReader.Create(stream, new XmlReaderSettings
                        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                        document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                    }
                    catch (XmlException) { /* Copy malformed bytes; the official parser owns its diagnostic. */ }
                }
                var edges = document?.Root?.Name == "RuleSet"
                    ? document.Root.Elements("Include").Where(include => include.Attribute("Path") is not null)
                        .Select(include => new RuleEdge(include.Attribute("Path") ?? throw Refusal("An included ruleset has no path operand."),
                            include.Attribute("Path")?.Value ?? "", include.Attribute("Action")?.Value)).ToImmutableArray()
                    : [];
                var state = new RuleStage(source, destination, bytes, document, edges);
                states.Add(source, state);
                return state;
            }

            async Task<string> VisitAsync(RuleStage state, bool isRoot)
            {
                if (state.Bytes is null || state.Document is null || !isRoot && !visited.Add(state.Source.ToLowerInvariant()))
                    return state.Destination;
                foreach (var edge in state.Edges)
                {
                    // RuleSet.GetEffectiveRuleSet skips suppressed includes BEFORE opening
                    // them. Preserve that authority boundary, including denied dead paths.
                    if (edge.Action == "None") continue;
                    var expanded = Environment.ExpandEnvironmentVariables(edge.Operand);
                    _expansions.Add(new(edge.Operand, expanded));
                    var parent = Path.GetDirectoryName(state.Source) ?? throw Refusal("A ruleset input has no physical directory.");
                    var child = await LoadAsync(Path.GetFullPath(expanded, parent)).ConfigureAwait(false);
                    if (child.Bytes is null && !OperatingSystem.IsWindows() && expanded.Contains('\\'))
                        child = await LoadAsync(Path.GetFullPath(expanded.Replace('\\', Path.DirectorySeparatorChar), parent)).ConfigureAwait(false);
                    // Relative, percent-free names prevent environment expansion from
                    // reinterpreting the private stage's parent directory spelling.
                    edge.Attribute.Value = Path.GetFileName(await VisitAsync(child, isRoot: false).ConfigureAwait(false));
                }
                return state.Destination;
            }
        }

        private sealed record RuleEdge(XAttribute Attribute, string Operand, string? Action);
        private sealed record RuleStage(string Source, string Destination, byte[]? Bytes, XDocument? Document, ImmutableArray<RuleEdge> Edges)
        {
            public override string ToString() => "CompilerRuleStage";
        }

    }

    // /analyzer shares the compiler's separated-path grammar without assembly
    // alias syntax or any parse-time filesystem access (including wildcards).
    private static IEnumerable<string> SeparatedPaths(string value, string baseDirectory) => CSharpCommandLineParser.Default
        .Parse(["-analyzer:" + value], baseDirectory, sdkDirectory: null).AnalyzerReferences.Select(reference => reference.FilePath);

    // Compiler path operands drop unescaped quotes and halve backslash runs only
    // immediately before a quote; spaces and all other backslashes remain literal.
    private static string Decode(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (value[index] == '"') { index++; continue; }
            if (value[index] != '\\') { result.Append(value[index++]); continue; }
            var start = index;
            while (index < value.Length && value[index] == '\\') index++;
            var count = index - start;
            var quote = index < value.Length && value[index] == '"';
            result.Append('\\', quote ? count / 2 : count);
            if (quote) { if (count % 2 != 0) result.Append('"'); index++; }
        }
        return result.ToString();
    }

    private static string QuotePath(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static bool TryOption(string argument, out string name, out string? value)
    {
        name = ""; value = null;
        if (argument.Length == 0 || argument[0] is not ('/' or '-')) return false;
        var colon = argument.IndexOf(':', 1);
        if (argument[0] == '/' && argument.IndexOf('/', 1, colon < 0 ? argument.Length - 1 : colon - 1) >= 0) return false;
        name = argument[1..(colon < 0 ? argument.Length : colon)];
        if (colon >= 0) value = argument[(colon + 1)..];
        return true;
    }

    private static PhysicalInputIdentity Identity(string path, FileContentDigest? digest) => new(path, digest is not null,
        digest is null ? [] : digest.Hash);
    private static PublicInvalidOperationException Refusal(string message) => new(message);
}

internal sealed record CompilerArgumentDirectory(string Path, string? Pattern, bool Recursive, PhysicalInputKind Kind,
    ImmutableArray<string> Matches, ImmutableArray<string> Traversed)
{
    public override string ToString() => $"CompilerArgumentDirectory {{ MatchCount = {Matches.Length}, DirectoryCount = {Traversed.Length} }}";
    internal static async Task<CompilerArgumentDirectory> CaptureAsync(string path, string? pattern, bool recursive,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
        => await CaptureCoreAsync(path, pattern, recursive, new CurrentBoundary(authorizePaths), ct).ConfigureAwait(false);

    internal Task<CompilerArgumentDirectory> CapturePhysicalAsync(CancellationToken ct)
        => CaptureCoreAsync(Path, Pattern, Recursive,
            new FrozenBoundary(Traversed.Concat(Matches).Append(Path).ToHashSet(FileSystemPath.Comparer)), ct);

    internal void AssertSame(CompilerArgumentDirectory current)
    {
        if (current.Kind != Kind || !current.Matches.OrderBy(path => path, FileSystemPath.Comparer).SequenceEqual(
                Matches.OrderBy(path => path, FileSystemPath.Comparer), FileSystemPath.Comparer)
            || !current.Traversed.OrderBy(path => path, FileSystemPath.Comparer).SequenceEqual(
                Traversed.OrderBy(path => path, FileSystemPath.Comparer), FileSystemPath.Comparer))
            throw new PublicInvalidOperationException("Compiler input directory membership changed after preparation; recreate the preview.");
    }

    private abstract class ObservationBoundary
    {
        internal abstract Task BeforeObservationAsync(string path, CancellationToken ct);
    }

    private sealed class CurrentBoundary(Func<IReadOnlyList<string>, CancellationToken, Task> authorize) : ObservationBoundary
    {
        internal override Task BeforeObservationAsync(string path, CancellationToken ct) => authorize([path], ct);
    }

    private sealed class FrozenBoundary(IReadOnlySet<string> paths) : ObservationBoundary
    {
        internal override Task BeforeObservationAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!paths.Contains(path))
                throw new PublicInvalidOperationException("Compiler input directory membership changed after preparation; recreate the preview.");
            return Task.CompletedTask;
        }
    }

    internal static PhysicalInputKind ProbePhysicalKind(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var coordinate = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var parentPath = System.IO.Path.GetDirectoryName(coordinate);
        using var parent = PhysicalDirectoryScope.OpenRouteIfPresent(parentPath ?? coordinate);
        if (parent is null) return PhysicalInputKind.Missing;
        parent.AssertCurrentPath();
        var kind = parentPath is null ? PhysicalDirectoryEntryKind.Directory
            : parent.GetEntryKind(System.IO.Path.GetFileName(coordinate), ct);
        parent.AssertCurrentPath();
        return kind switch
        {
            null => PhysicalInputKind.Missing,
            PhysicalDirectoryEntryKind.Directory => PhysicalInputKind.Directory,
            PhysicalDirectoryEntryKind.File => PhysicalInputKind.File,
            _ => throw WorkspaceManager.PhysicalPathRequired()
        };
    }

    private static async Task<CompilerArgumentDirectory> CaptureCoreAsync(string path, string? pattern, bool recursive,
        ObservationBoundary boundary, CancellationToken ct)
    {
        var files = ImmutableArray.CreateBuilder<string>();
        var directories = ImmutableArray.CreateBuilder<string>();
        try
        {
            var kind = await ProbeAsync(path).ConfigureAwait(false);
            if (pattern is not null && kind == PhysicalInputKind.Directory)
            {
                using var root = PhysicalDirectoryScope.OpenRoute(path);
                await VisitAsync(root).ConfigureAwait(false);
            }
            return new(path, pattern, recursive, kind, files.ToImmutable(), directories.ToImmutable());
        }
        catch (IOException failure) { throw new PublicInvalidOperationException("A compiler search directory changed or could not be listed (" + failure.GetType().Name + ")."); }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("A compiler search directory could not be observed under current authority."); }

        async Task<PhysicalInputKind> ProbeAsync(string directory)
        {
            await boundary.BeforeObservationAsync(directory, ct).ConfigureAwait(false);
            return ProbePhysicalKind(directory, ct);
        }
        async Task AuthorizeHeldAsync(PhysicalDirectoryScope directory)
        {
            await boundary.BeforeObservationAsync(directory.Path, ct).ConfigureAwait(false);
            directory.AssertCurrentPath();
        }
        async Task VisitAsync(PhysicalDirectoryScope directory)
        {
            ct.ThrowIfCancellationRequested();
            await AuthorizeHeldAsync(directory).ConfigureAwait(false);
            directories.Add(directory.Path);
            // Entries and kinds are metadata from the held, no-follow namespace.
            // Never open a file merely to decide whether its name matches.
            var entries = directory.Entries().Select(name => (Name: name, Kind: directory.GetEntryKind(name, ct)
                ?? throw new PublicInvalidOperationException("A compiler directory entry disappeared while its membership was captured."))).ToArray();
            foreach (var entry in entries)
            {
                if (entry.Kind == PhysicalDirectoryEntryKind.Directory || pattern == "." || !Matches(entry.Name)) continue;
                var file = System.IO.Path.Combine(directory.Path, entry.Name);
                await boundary.BeforeObservationAsync(file, ct).ConfigureAwait(false);
                directory.AssertCurrentPath();
                if (entry.Kind is PhysicalDirectoryEntryKind.Link or PhysicalDirectoryEntryKind.Other)
                    throw new PublicInvalidOperationException("Compiler argument authority requires selected inputs to be physical regular files.");
                files.Add(file);
            }
            if (!recursive) return;
            foreach (var entry in entries)
            {
                if (entry.Kind is not (PhysicalDirectoryEntryKind.Directory or PhysicalDirectoryEntryKind.Link)) continue;
                var childPath = System.IO.Path.Combine(directory.Path, entry.Name);
                await boundary.BeforeObservationAsync(childPath, ct).ConfigureAwait(false);
                directory.AssertCurrentPath();
                // A no-follow link cannot be classified by following its target
                // under the lexical parent grant. Semantic-only loading remains available.
                if (entry.Kind == PhysicalDirectoryEntryKind.Link)
                    throw new PublicInvalidOperationException("Compiler argument authority cannot recursively traverse a physical alias; use an alias-free source tree.");
                using var child = directory.OpenChild(entry.Name)
                    ?? throw new PublicInvalidOperationException("A compiler input directory changed while its membership was captured.");
                await VisitAsync(child).ConfigureAwait(false);
            }
        }
        bool Matches(string name)
        {
            var expression = pattern ?? throw new PublicInvalidOperationException("A compiler enumeration has no captured pattern.");
            if (expression.Length == 0 || expression == "*.*") expression = "*";
            if (!OperatingSystem.IsWindows())
                expression = expression.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal).Replace(">", "\\>", StringComparison.Ordinal).Replace("<", "\\<", StringComparison.Ordinal);
            return FileSystemName.MatchesWin32Expression(FileSystemName.TranslateWin32Expression(expression), name, OperatingSystem.IsWindows());
        }
    }

}

internal enum CompilerArgumentReferenceKind { MetadataFile, NativeProject }

internal sealed record CompilerArgumentReference(string Path, MetadataReferenceProperties Properties, CompilerArgumentReferenceKind Kind)
{
    public override string ToString() => "CompilerArgumentReference";
}

internal sealed record CompilerRulesetExpansion(string Operand, string Expanded)
{
    public override string ToString() => "CompilerRulesetExpansion";
}

internal sealed record CompilerArgumentReferenceReceipt(ImmutableArray<CompilerArgumentReference> References, CompilerArgumentParseReceipt Parsing)
{
    public override string ToString() => $"CompilerArgumentReferenceReceipt {{ ReferenceCount = {References.Length} }}";
}

[Flags]
internal enum CompilerArgumentInputRole
{
    Response = 1,
    Ruleset = 2,
    MetadataProbe = 4,
    SourceMatch = 8,
    AdditionalMatch = 16,
    AnalyzerConfig = 32,
    EmbeddedMatch = 64
}

internal sealed record CompilerArgumentInputObservation(PhysicalInputIdentity Input, CompilerArgumentInputRole Roles)
{
    internal static void RequireRole(CompilerArgumentInputRole role)
    {
        const CompilerArgumentInputRole known = CompilerArgumentInputRole.Response | CompilerArgumentInputRole.Ruleset
            | CompilerArgumentInputRole.MetadataProbe | CompilerArgumentInputRole.SourceMatch
            | CompilerArgumentInputRole.AdditionalMatch | CompilerArgumentInputRole.AnalyzerConfig | CompilerArgumentInputRole.EmbeddedMatch;
        if (role == 0 || (role & ~known) != 0)
            throw new ArgumentOutOfRangeException(nameof(role), "Compiler input observations require known nonzero roles.");
    }
}

internal sealed class CompilerArgumentParseReceipt
{
    private readonly ImmutableArray<CompilerArgumentDirectory> directories;
    private readonly ImmutableArray<CompilerRulesetExpansion> expansions;

    internal CompilerArgumentParseReceipt(CSharpCommandLineArguments arguments,
        IEnumerable<CompilerArgumentInputObservation> observations,
        ImmutableArray<CompilerArgumentDirectory> directories, ImmutableArray<CompilerRulesetExpansion> expansions)
    {
        var captured = observations.ToArray();
        Inputs = PhysicalInputIdentity.MergeConsistent(captured.Select(observation => observation.Input));
        var roles = new Dictionary<string, CompilerArgumentInputRole>(FileSystemPath.Comparer);
        foreach (var observation in captured)
        {
            CompilerArgumentInputObservation.RequireRole(observation.Roles);
            roles.TryGetValue(observation.Input.FilePath, out var prior);
            roles[observation.Input.FilePath] = prior | observation.Roles;
        }
        Observations = Inputs.Select(input => new CompilerArgumentInputObservation(input, roles[input.FilePath])).ToImmutableArray();
        Arguments = arguments;
        this.directories = directories;
        this.expansions = expansions;
        CurrentGrantPaths = GrantPaths(Inputs, directories);
    }

    internal CSharpCommandLineArguments Arguments { get; }
    internal ImmutableArray<PhysicalInputIdentity> Inputs { get; }
    internal ImmutableArray<CompilerArgumentInputObservation> Observations { get; }
    internal IReadOnlyList<string> CurrentGrantPaths { get; }
    internal static IReadOnlyList<string> GrantPaths(ImmutableArray<PhysicalInputIdentity> inputs,
        ImmutableArray<CompilerArgumentDirectory> directories)
        => inputs.Select(input => input.FilePath)
            .Concat(directories.SelectMany(directory => directory.Traversed.Concat(directory.Matches).Append(directory.Path)))
            .Distinct(FileSystemPath.Comparer).ToArray();

    internal ImmutableArray<CompilerArgumentDirectory> Directories => directories;
    internal ImmutableArray<CompilerRulesetExpansion> Expansions => expansions;
    internal IEnumerable<string> RecursiveTraversalRoots => directories.Where(directory => directory.Recursive).Select(directory => directory.Path);
    public override string ToString() => $"CompilerArgumentParseReceipt {{ InputCount = {Inputs.Length}, DirectoryCount = {directories.Length} }}";
    internal CompilerArgumentParseReceipt WithInputs(IEnumerable<CompilerArgumentInputObservation> additional) => new(Arguments,
        Observations.Concat(additional), directories, expansions);
    internal Task AssertCurrentAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest, CancellationToken ct)
        => AssertInputsAsync(Inputs, directories, expansions, authorizePaths, readDigest, ct);

    /// <summary>The caller renews the complete CurrentGrantPaths batch before this callback-free physical proof.</summary>
    internal Task AssertPhysicalCurrentAsync(IReadOnlySet<string> excludedInputs, CancellationToken ct)
        => AssertPhysicalInputsAsync(Inputs, directories, expansions, excludedInputs, ct);

    internal static async Task AssertPhysicalInputsAsync(ImmutableArray<PhysicalInputIdentity> inputs,
        ImmutableArray<CompilerArgumentDirectory> directories, ImmutableArray<CompilerRulesetExpansion> expansions,
        IReadOnlySet<string> excludedInputs, CancellationToken ct)
    {
        AssertExpansions(expansions, ct);
        foreach (var directory in directories)
            directory.AssertSame(await directory.CapturePhysicalAsync(ct).ConfigureAwait(false));
        // Digests close the observation sequence after native directory membership.
        // Exclusions belong to separately-owned mutable file epochs; directory and
        // environment receipts always remain part of this proof.
        foreach (var input in inputs)
        {
            if (input.Kind != PhysicalInputKind.Directory && excludedInputs.Contains(input.FilePath)) continue;
            if (CompilerArgumentDirectory.ProbePhysicalKind(input.FilePath, ct) != input.Kind)
                throw new PublicInvalidOperationException("Physical compiler argument input kinds changed after preparation; recreate the preview.");
            if (input.Kind != PhysicalInputKind.File) continue;
            var digest = await PhysicalInputReader.ReadDigestAsync(input.FilePath, ct).ConfigureAwait(false);
            if (!digest.Hash.AsSpan().SequenceEqual(input.ContentHash.AsSpan()))
                throw new PublicInvalidOperationException("Physical compiler argument inputs changed after preparation; recreate the preview.");
        }
    }

    internal static void AssertExpansions(ImmutableArray<CompilerRulesetExpansion> expansions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var expansion in expansions)
            if (!string.Equals(Environment.ExpandEnvironmentVariables(expansion.Operand), expansion.Expanded, StringComparison.Ordinal))
                throw new PublicInvalidOperationException("Compiler ruleset include expansion changed after preparation; recreate the preview.");
    }
    internal static async Task AssertInputsAsync(ImmutableArray<PhysicalInputIdentity> inputs, ImmutableArray<CompilerArgumentDirectory> directories,
        ImmutableArray<CompilerRulesetExpansion> expansions,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, Func<string, CancellationToken, Task<FileContentDigest?>> readDigest, CancellationToken ct)
    {
        AssertExpansions(expansions, ct);
        await authorizePaths(inputs.Select(input => input.FilePath).Concat(directories.Select(directory => directory.Path)).ToArray(), ct).ConfigureAwait(false);
        foreach (var input in inputs)
        {
            var actual = await CompilerArgumentDirectory.CaptureAsync(input.FilePath, null, false, authorizePaths, ct).ConfigureAwait(false);
            if (actual.Kind != input.Kind)
                throw new PublicInvalidOperationException("Physical compiler argument input kinds changed after preparation; recreate the preview.");
            if (input.Kind != PhysicalInputKind.File) continue;
            await authorizePaths([input.FilePath], ct).ConfigureAwait(false);
            WorkspaceManager.EnsurePhysicalPath(input.FilePath);
            var digest = await readDigest(input.FilePath, ct).ConfigureAwait(false);
            if (input.Exists != (digest is not null) || digest is not null && !digest.Hash.AsSpan().SequenceEqual(input.ContentHash.AsSpan()))
                throw new PublicInvalidOperationException("Physical compiler argument inputs changed after preparation; recreate the preview.");
        }
        foreach (var directory in directories)
        {
            var current = await CompilerArgumentDirectory.CaptureAsync(directory.Path, directory.Pattern, directory.Recursive, authorizePaths, ct).ConfigureAwait(false);
            directory.AssertSame(current);
        }
    }
}
