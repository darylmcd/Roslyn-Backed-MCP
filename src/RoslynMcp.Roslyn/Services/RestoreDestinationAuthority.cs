using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Freezes native NuGet destination selection without changing nullable recorded CLI overrides.</summary>
internal sealed class RestoreDestinationAuthority
{
    private readonly List<string> _destinations = [];
    private readonly List<PhysicalInputIdentity> _configs = [];
    private readonly List<Func<CancellationToken, Task>> _assertSettings = [];
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _authorize;
    private RestoreDestinationAuthority(Func<IReadOnlyList<string>, CancellationToken, Task> authorize) => _authorize = authorize;

    internal static async Task<RestoreDestinationAuthority> CaptureAsync(IReadOnlyList<EvaluatedBuildGraph.Node> nodes,
        IReadOnlyList<RestoreInvocationPlanner.ProjectRestore> projects,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var result = new RestoreDestinationAuthority(authorize);
        var contexts = projects.ToList();
        foreach (var invocation in RestoreInvocationPlanner.Plan(nodes.FirstOrDefault()?.ProjectPath
            ?? throw Refusal("Restore has no captured native project context."), projects))
        {
            if (invocation.Context is { } context && !contexts.Any(existing =>
                FileSystemPath.Comparer.Equals(existing.ProjectPath, context.ProjectPath)
                && existing.EvaluationProperties.Count == context.EvaluationProperties.Count
                && existing.EvaluationProperties.All(pair => context.EvaluationProperties.TryGetValue(pair.Key, out var value) && value == pair.Value)))
                contexts.Add(context);
        }
        foreach (var project in contexts)
        {
            ct.ThrowIfCancellationRequested();
            var matchingNodes = nodes.Where(candidate => FileSystemPath.Comparer.Equals(candidate.ProjectPath, project.ProjectPath)).ToArray();
            if (matchingNodes.Length == 0 || matchingNodes.Select(candidate => candidate.TaskRegistrations.ToolsVersion).Distinct(StringComparer.Ordinal).Count() != 1)
                throw Refusal("Restore has no unambiguous native tools context.");
            var node = matchingNodes.First();
            if (project.PackagesPath is { } recorded && !node.TaskRegistrations.Registrations.Any(registration =>
                NativeTaskExecutionIdentity.CanonicalTask(registration.Name) == "GetRestoreSettingsTask"))
            {
                result._destinations.Add(recorded);
                continue;
            }
            var globals = project.PackagesPath is { } packages
                ? project.EvaluationProperties.SetItem("RestorePackagesPath", Microsoft.Build.Evaluation.ProjectCollection.Escape(packages))
                : project.EvaluationProperties;
            var (native, _) = await NativeCompilerTaskInputs.ReadRawAsync(project.ProjectPath, globals,
                project.TargetFramework, node.TaskRegistrations.ToolsVersion, ["_GetRestoreSettings"],
                NativeCompilerTaskInputs.CapturePurpose.RestoreSettings, ct).ConfigureAwait(false);
            var expectedContext = NativeTaskExecutionIdentity.EscapedContextIdentity(globals, node.TaskRegistrations.ToolsVersion, ct);
            var receipts = native.RestoreSettings.Where(receipt => FileSystemPath.Comparer.Equals(receipt.Project, project.ProjectPath)
                && receipt.Context == expectedContext).ToArray();
            if (receipts.Length != 1) throw Refusal("The native restore context did not establish exactly one settings producer.");
            var settings = receipts[0];
            if (!FileSystemPath.Comparer.Equals(settings.Assembly, Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "NuGet.Build.Tasks.dll"))
                || settings.Implementation != "NuGet.Build.Tasks.GetRestoreSettingsTask")
                throw Refusal("The native restore settings producer is not the registered SDK task.");
            string Scalar(string name) => settings.Parameters.TryGetValue(name, out var values)
                ? values.Length == 1 ? values[0] : throw Refusal("A native restore setting is ambiguous.") : "";
            var destination = Scalar("OutputPackagesPath");
            if (!Path.IsPathFullyQualified(destination)) throw Refusal("Native restore settings did not establish an absolute destination.");
            if (project.PackagesPath is { } required && !FileSystemPath.Comparer.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(required)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination))))
                throw Refusal("Native restore settings do not preserve the recorded package destination.");
            result._destinations.Add(Path.GetFullPath(destination));
            // Selected configurations are discovery only. Current grants precede all
            // product-owned metadata/byte reads, including absent nearer candidates.
            var paths = settings.Parameters.GetValueOrDefault("OutputConfigFilePaths") ?? [];
            if (paths.Any(path => !Path.IsPathFullyQualified(path)))
                throw Refusal("Native restore configuration paths have no absolute identity.");
            var candidates = paths.Select(Path.GetFullPath).ToHashSet(FileSystemPath.Comparer);
            if (Scalar("RestoreConfigFile").Length == 0)
            {
                var root = Scalar("RestoreRootConfigDirectory");
                if (root.Length == 0) root = Path.GetDirectoryName(project.ProjectPath)!;
                var solution = Scalar("RestoreSolutionDirectory");
                // NuGet RestoreSettingsUtils.ReadSettings searches the solution's
                // .nuget directory before walking its ancestors (PackagesConfig).
                if (solution.Length > 0) root = Path.Combine(solution, ".nuget");
                root = Path.GetFullPath(root, Path.GetDirectoryName(project.ProjectPath)!);
                var names = settings.Parameters.GetValueOrDefault("NativeSupportedConfigNames")
                    ?? throw Refusal("Native restore settings did not establish the configuration filename protocol.");
                if (names.Length == 0 || names.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name
                    || name.IndexOfAny(['/', '\\', '*', '?', ':']) >= 0 || name is "." or ".."))
                    throw Refusal("Native restore settings returned an invalid configuration filename protocol.");
                for (var directory = root; directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    foreach (var name in names) candidates.Add(Path.Combine(directory, name));
                }
            }
            await authorize(candidates.ToArray(), ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
                result._configs.Add(await PhysicalInputIdentity.CaptureAsync(candidate, ct).ConfigureAwait(false));
            result._assertSettings.Add(async token =>
            {
                var (currentNative, _) = await NativeCompilerTaskInputs.ReadRawAsync(project.ProjectPath, globals,
                    project.TargetFramework, node.TaskRegistrations.ToolsVersion, ["_GetRestoreSettings"],
                    NativeCompilerTaskInputs.CapturePurpose.RestoreSettings, token).ConfigureAwait(false);
                var current = currentNative.RestoreSettings
                    .Where(receipt => FileSystemPath.Comparer.Equals(receipt.Project, settings.Project) && receipt.Context == settings.Context).ToArray();
                if (current.Length != 1 || current[0].Assembly != settings.Assembly || current[0].Implementation != settings.Implementation
                    || current[0].Parameters.Count != settings.Parameters.Count
                    || settings.Parameters.Any(pair => !current[0].Parameters.TryGetValue(pair.Key, out var values)
                        || !pair.Value.SequenceEqual(values, StringComparer.Ordinal)))
                    throw Refusal("Native restore settings changed; prepare restore again.");
            });
        }
        await result.AssertCurrentAsync(ct).ConfigureAwait(false);
        return result;
    }

    internal IReadOnlyList<PhysicalInputIdentity> ConfigInputs => _configs;
    internal IEnumerable<string> CurrentGrantPaths => _destinations
        .Where(path => !BuildInputProvenance.IsBootstrapRestoreDestination(path))
        .Concat(_configs.Select(input => input.FilePath));
    internal async Task AssertNativeSettingsCurrentAsync(CancellationToken ct)
    {
        foreach (var assertSettings in _assertSettings) await assertSettings(ct).ConfigureAwait(false);
    }
    internal void AssertPhysicalDestinations(CancellationToken ct)
    {
        foreach (var path in _destinations)
        {
            ct.ThrowIfCancellationRequested();
            WorkspaceManager.EnsurePhysicalPath(path);
        }
    }

    internal async Task AssertCurrentAsync(CancellationToken ct)
    {
        await BuildInputProvenance.AuthorizeRestoreDestinationsAsync(_destinations, _authorize, ct).ConfigureAwait(false);
        await _authorize(_configs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(_configs, ct).ConfigureAwait(false);
        foreach (var assertSettings in _assertSettings) await assertSettings(ct).ConfigureAwait(false);
        // A native settings query is discovery; it must not recertify changed bytes.
        await _authorize(_configs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(_configs, ct).ConfigureAwait(false);
    }
    private static PublicInvalidOperationException Refusal(string message) => new(message);
}
