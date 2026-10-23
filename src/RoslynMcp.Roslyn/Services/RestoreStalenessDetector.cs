using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Detects whether a loaded workspace is stale with respect to <c>dotnet restore</c> — both the
/// in-flight-restore race (concurrent out-of-process restore mutating <c>obj/</c> artefacts while
/// MSBuild opens the solution) and the after-the-fact drift check (installed package versions in
/// <c>project.assets.json</c> no longer matching the evaluated project's requested versions).
/// Uses the captured evaluation context and active dependency graph; known package expectations
/// require restore when their assets cannot be read or verified.
/// </summary>
/// <remarks>
/// Not sealed so a future failure-path test double can derive and override the virtual surface,
/// mirroring <see cref="WorkspaceSessionLoader"/>'s pattern. <c>ILogger</c> is a per-call
/// parameter (not constructor-injected) since <see cref="WorkspaceManager"/> already owns the
/// logger instance for the session and this detector has no independent lifetime.
/// </remarks>
internal class RestoreStalenessDetector
{
    /// <summary>
    /// Interval between mtime samples inside the restore-race stability probe. Chosen so the
    /// stable window is roughly 250 ms (two samples separated by this interval) — long enough
    /// that a `dotnet restore` in its final write phase cannot squeeze an asset rewrite inside
    /// it, short enough that a no-op pre-check returns within ~1.5 × <c>StableWindowMs</c>.
    /// </summary>
    private const int RestoreRaceSampleIntervalMs = 125;

    /// <summary>
    /// Required stable window (in milliseconds) that every detected restore artefact must
    /// hold its mtime for before <see cref="WorkspaceManager.LoadIntoSessionAsync"/> hands the
    /// solution to MSBuild. Two consecutive samples separated by
    /// <see cref="RestoreRaceSampleIntervalMs"/> produce a ~250 ms window.
    /// </summary>
    private const int RestoreRaceStableWindowMs = 250;

    /// <summary>
    /// dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz — best-effort wait for a
    /// concurrent out-of-process <c>dotnet restore</c> to finish before MSBuild opens the
    /// solution.
    /// </summary>
    /// <remarks>
    /// Evaluates active project/dependency restore inputs with the captured globals, then polls
    /// their actual assets and dependency-specification paths, polls their <see cref="File.GetLastWriteTimeUtc"/>, and returns once
    /// every file's mtime has been stable for <see cref="RestoreRaceStableWindowMs"/> ms — or
    /// the <paramref name="restoreRaceWaitMs"/> cap fires. No-op when the cap is zero, when no
    /// artefacts exist (typical on a pristine checkout before first build), or when all
    /// artefacts are already stable on the first sample (typical on a healthy load after a
    /// completed restore).
    /// </remarks>
    public virtual async Task WaitForStableRestoreArtifactsAsync(
        string fullPath,
        IReadOnlyDictionary<string, string> globals,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize,
        int restoreRaceWaitMs,
        ILogger logger,
        CancellationToken ct)
    {
        var capMs = restoreRaceWaitMs;
        if (capMs <= 0)
        {
            return;
        }

        var rootDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(rootDirectory)) return;
        await authorize([fullPath, rootDirectory], ct).ConfigureAwait(false);
        using var rootScope = PhysicalDirectoryScope.OpenRouteIfPresent(rootDirectory);
        if (rootScope is null) return;
        rootScope.AssertCurrentPath();

        // Capture evaluated locations before sampling; an old obj tree must not shadow a
        // configured intermediate directory or a dependency outside the solution folder.
        var artefacts = RestoreArtifactLocator.Find(fullPath, globals, ct);
        rootScope.AssertCurrentPath();
        if (artefacts.Count == 0) return;
        await authorize(artefacts, ct).ConfigureAwait(false);
        if (!artefacts.Any(path => ReadLastWriteTimeUtc(path, ct) != DateTime.MinValue))
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(capMs);
        var lastSnapshot = new Dictionary<string, DateTime>(artefacts.Count, FileSystemPath.Comparer);
        var stableSince = new Dictionary<string, DateTime>(artefacts.Count, FileSystemPath.Comparer);

        // Seed the snapshot. A file that does not exist yet is tracked as DateTime.MinValue
        // so the stability check catches the "appears mid-poll" case (restore creating the
        // first project.assets.json while we race it).
        var seedNow = DateTime.UtcNow;
        await authorize(artefacts, ct).ConfigureAwait(false);
        foreach (var path in artefacts)
        {
            lastSnapshot[path] = ReadLastWriteTimeUtc(path, ct);
            stableSince[path] = seedNow;
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var allStable = true;
            await authorize(artefacts, ct).ConfigureAwait(false);

            foreach (var path in artefacts)
            {
                var currentMtime = ReadLastWriteTimeUtc(path, ct);
                if (currentMtime != lastSnapshot[path])
                {
                    // Mtime moved — reset the stability window for this file.
                    lastSnapshot[path] = currentMtime;
                    stableSince[path] = now;
                    allStable = false;
                    continue;
                }

                if ((now - stableSince[path]).TotalMilliseconds < RestoreRaceStableWindowMs)
                {
                    allStable = false;
                }
            }

            if (allStable)
            {
                return;
            }

            if (now >= deadline)
            {
                logger.LogWarning(
                    "workspace_load: restore-race wait hit {CapMs} ms cap for '{Path}' without reaching a stable mtime window. Proceeding with load — callers may observe CS1705 drift; re-run workspace_reload after the concurrent restore finishes if so.",
                    capMs,
                    MsBuildCommandProperties.Redact(fullPath, MsBuildCommandProperties.Append([], globals)));
                return;
            }

            // Bound the delay so we never overshoot the deadline by more than one interval.
            var remainingMs = (int)Math.Max(0, (deadline - now).TotalMilliseconds);
            var delayMs = Math.Min(RestoreRaceSampleIntervalMs, remainingMs);
            if (delayMs == 0)
            {
                // Last-iteration guard: if we've arrived at the deadline, take one final
                // reading on the next loop and exit via the deadline branch.
                continue;
            }

            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }

    private static DateTime ReadLastWriteTimeUtc(string path, CancellationToken ct)
    {
        try
        {
            return PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(path, ct) ?? DateTime.MinValue;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    public virtual async Task<bool> DetectRestoreRequiredAsync(Microsoft.CodeAnalysis.Solution solution,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, ILogger logger, CancellationToken ct)
    {
        var selectingPaths = solution.Projects.Select(project => project.FilePath).OfType<string>()
            .Concat(solution.FilePath is { } entry ? new[] { entry } : []).Distinct(FileSystemPath.Comparer).ToArray();
        await authorize(selectingPaths, ct).ConfigureAwait(false);
        EvaluatedBuildGraph.Graph graph;
        try { graph = EvaluatedBuildGraph.Source(solution, ct); }
        catch (InvalidOperationException failure)
        {
            logger.LogDebug("Restore graph evaluation unavailable ({FailureType})", failure.GetType().Name);
            return false;
        }
        foreach (var node in graph.Nodes.Where(node => !node.IsOuterBuild))
        {
            var context = node.Inputs;
            if (context.Packages.Count == 0) continue;
            if (context.AssetsPath is not { } assetsPath) return true;
            await authorize([assetsPath], ct).ConfigureAwait(false);
            try
            {
                using var input = PhysicalInputReader.OpenRead(assetsPath, ct);
                using var document = await JsonDocument.ParseAsync(input.Stream, cancellationToken: ct).ConfigureAwait(false);
                if (PackagesRequireRestore(context, ParseAssetsPackageVersions(context, document))) return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                logger.LogDebug("restore-drift assets could not be read ({FailureType}); restore is required.", failure.GetType().Name);
                return true;
            }
        }
        return false;
    }

    public virtual bool IsRestoreRequired(string projectFilePath, IReadOnlyDictionary<string, string> globals, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath)) return false;
        try
        {
            var graph = EvaluatedBuildGraph.FromRoots([projectFilePath], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            return EvaluatedBuildGraph.ActiveEntries(graph)
                .Any(node => ContextRequiresRestoreSafely(node.Inputs, logger));
        }
        catch (InvalidOperationException failure)
        {
            logger.LogDebug("Restore input evaluation unavailable ({FailureType})", failure.GetType().Name);
            return false;
        }
    }

    private static bool ContextRequiresRestoreSafely(MsBuildMetadataHelper.RestoreInputs context, ILogger logger)
    {
        try
        {
            if (context.Packages.Count == 0) return false;
            return PackagesRequireRestore(context, LoadAssetsPackageVersions(context));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Evaluated package expectations are known; unreadable assets cannot establish freshness.
            // Report only the failure type, never assets content, paths or exception text.
            logger.LogDebug("restore-drift assets could not be read ({FailureType}); restore is required.",
                failure.GetType().Name);
            return true;
        }
    }

    private static bool PackagesRequireRestore(MsBuildMetadataHelper.RestoreInputs context,
        (Dictionary<string, HashSet<string>> PackageVersions, Dictionary<string, HashSet<string>> CentralPackageVersions)? assets)
    {
        if (assets is null) return true;
        foreach (var package in context.Packages)
        {
            if (!assets.Value.PackageVersions.TryGetValue(package.Id, out var actual) ||
                !string.IsNullOrWhiteSpace(package.RequestedVersion) && !PackageVersionMatches(package.RequestedVersion, actual)) return true;
            if (package.UsesCentralVersion && (string.IsNullOrWhiteSpace(package.RequestedVersion) ||
                !assets.Value.CentralPackageVersions.TryGetValue(package.Id, out var central) ||
                !PackageVersionMatches(package.RequestedVersion, central))) return true;
        }
        return false;
    }

    /// <summary>
    /// workspace-restore-packages-path: reads <c>project.restore.packagesPath</c> from the project's
    /// existing <c>obj/project.assets.json</c> — the package folder the last restore wrote to (for
    /// example a worktree scratch folder). Returns <see langword="null"/> when the assets file, the
    /// property, or a non-empty string value is absent (first restore) or the file cannot be read.
    /// </summary>
    public static string? TryReadRestorePackagesPath(string projectFilePath, IReadOnlyDictionary<string, string> globals)
    {
        try { return ReadRequiredRestorePackagesPathFromAssets(GetExistingAssetsPath(projectFilePath, globals), CancellationToken.None); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    internal static string? ReadRequiredRestorePackagesPathFromAssets(string? assetsPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (assetsPath is null) return null;
        try
        {
            using var input = PhysicalInputReader.OpenRead(assetsPath, ct);
            using var document = JsonDocument.Parse(input.Stream);
            ct.ThrowIfCancellationRequested();
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidAssets();
            if (!root.TryGetProperty("project", out var project)) return null;
            if (project.ValueKind != JsonValueKind.Object) throw InvalidAssets();
            if (!project.TryGetProperty("restore", out var restore)) return null;
            if (restore.ValueKind != JsonValueKind.Object) throw InvalidAssets();
            if (!restore.TryGetProperty("packagesPath", out var packagesPath)) return null;
            if (packagesPath.ValueKind != JsonValueKind.String) throw InvalidAssets();
            return string.IsNullOrWhiteSpace(packagesPath.GetString()) ? null : packagesPath.GetString();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw InvalidAssets();
        }

        static RoslynMcp.Core.Services.PublicInvalidOperationException InvalidAssets() => new(
            "The existing restore assets do not establish readable package-root authority. Repair or explicitly remove the unusable assets, restore the project, and retry.");
    }

    /// <summary>
    /// workspace-load-missing-assets-auto-restore: true when the project declares at least one
    /// <c>PackageReference</c> but has no <c>project.assets.json</c> at all — the "never restored"
    /// state, distinct from version drift (which <see cref="IsRestoreRequired"/> also reports). The
    /// assets file is located where MSBuild would write it, so <c>UseArtifactsOutput</c> and a
    /// custom <c>BaseIntermediateOutputPath</c>/<c>MSBuildProjectExtensionsPath</c> are not
    /// misread as missing. Like <see cref="IsRestoreRequired"/>, detection is best-effort: an
    /// missing, unreadable or malformed definitions return false. A readable well-formed definition
    /// whose evaluation fails conservatively requires restore; unknown evaluation is not healthy.
    /// The authoritative graph/restore path reports the precise safe evaluation refusal.
    /// </summary>
    internal static async Task<bool> HasMissingAssetsAsync(EvaluatedBuildGraph.Graph graph,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        foreach (var node in graph.Nodes.Where(node => !node.IsOuterBuild && node.Inputs.Packages.Count > 0))
        {
            if (node.Inputs.AssetsPath is not { } path) return true;
            await authorize([path], ct).ConfigureAwait(false);
            if (PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(path, ct) is null) return true;
        }
        return false;
    }

    public static bool HasMissingAssets(string projectFilePath, IReadOnlyDictionary<string, string> globals, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath)) return false;
        var readableDefinition = false;
        try
        {
            // Establish readability only. Never infer active items or dependency edges from XML.
            XmlFileLoader.Load(projectFilePath, System.Xml.Linq.LoadOptions.None);
            readableDefinition = true;
            return MsBuildMetadataHelper.EvaluateProjectContexts(projectFilePath, globals)
                .Any(context => context.Packages.Count > 0 && (context.AssetsPath is null || !File.Exists(context.AssetsPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or JsonException or InvalidOperationException)
        {
            var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], globals));
            logger.LogDebug(redactor.Failure(ex), "missing-assets detection could not establish {FailureStage} for '{ProjectFilePath}'; restore required: {RestoreRequired}",
                readableDefinition ? "evaluated inputs" : "readable XML", redactor.Text(projectFilePath), readableDefinition);
            return readableDefinition;
        }
    }

    private static string? GetExistingAssetsPath(string projectFilePath, IReadOnlyDictionary<string, string> globals)
    {
        var path = MsBuildMetadataHelper.ResolveAssetsPath(projectFilePath, globals);
        return path is not null && File.Exists(path) ? path : null;
    }

    private static (Dictionary<string, HashSet<string>> PackageVersions, Dictionary<string, HashSet<string>> CentralPackageVersions)? LoadAssetsPackageVersions(MsBuildMetadataHelper.RestoreInputs context)
    {
        var assetsPath = context.AssetsPath;
        if (assetsPath is null || !File.Exists(assetsPath))
        {
            return null;
        }

        using var stream = File.OpenRead(assetsPath);
        using var document = JsonDocument.Parse(stream);
        return ParseAssetsPackageVersions(context, document);
    }

    private static (Dictionary<string, HashSet<string>> PackageVersions, Dictionary<string, HashSet<string>> CentralPackageVersions)? ParseAssetsPackageVersions(
        MsBuildMetadataHelper.RestoreInputs context, JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !ProjectMetadataParser.TryGetAssetsFramework(document.RootElement, string.IsNullOrWhiteSpace(context.TargetFramework) ? context.TargetFrameworkMoniker : context.TargetFramework, out _, out var selectedFramework)) return null;

        var packageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var centralPackageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in new[] { selectedFramework })
        {

            if (framework.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.Object ||
                        !dependency.Value.TryGetProperty("version", out var versionElement) ||
                        versionElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    AddVersion(packageVersions, dependency.Name, versionElement.GetString());
                }
            }

            if (framework.TryGetProperty("centralPackageVersions", out var centralVersions) &&
                centralVersions.ValueKind == JsonValueKind.Object)
            {
                foreach (var centralVersion in centralVersions.EnumerateObject())
                {
                    if (centralVersion.Value.ValueKind == JsonValueKind.String)
                        AddVersion(centralPackageVersions, centralVersion.Name, centralVersion.Value.GetString());
                }
            }
        }

        return (packageVersions, centralPackageVersions);
    }

    private static void AddVersion(Dictionary<string, HashSet<string>> versions, string packageId, string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        if (!versions.TryGetValue(packageId, out var values))
        {
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            versions[packageId] = values;
        }

        values.Add(version.Trim());
    }

    private static bool PackageVersionMatches(string expectedVersion, IReadOnlySet<string> actualVersions)
    {
        if (!NuGet.Versioning.VersionRange.TryParse(expectedVersion, out var expected)) return false;
        return actualVersions.Any(text => NuGet.Versioning.VersionRange.TryParse(text, out var actual)
            && NuGet.Versioning.VersionRangeComparer.VersionRelease.Equals(expected, actual)
            && Equals(expected.Float, actual.Float));
    }

    // restore-required-vs-build-conflation: a WORKSPACE_UNRESOLVED_ANALYZER warning indicates a
    // missing BUILD output (e.g. an analyzer dll not yet produced by `dotnet build`), NOT a
    // missing NuGet package. It must NOT set restoreRequired (which routes callers to a no-op
    // `dotnet restore` loop and arms autoRestore for a pointless restore). It is detected
    // separately by WorkspaceDiagnosticClassifier and surfaced via the BuildRequired flag.
    public static bool HasRestoreRequiredWorkspaceDiagnostics(IEnumerable<DiagnosticDto> diagnostics)
    {
        var suspiciousDiagnostics = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (string.Equals(diagnostic.Id, "CS0234", StringComparison.OrdinalIgnoreCase))
            {
                suspiciousDiagnostics++;
                continue;
            }

            var message = diagnostic.Message;
            if (message is null)
            {
                continue;
            }

            if (message.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("does not exist in the namespace", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("could not load file or assembly", StringComparison.OrdinalIgnoreCase))
            {
                suspiciousDiagnostics++;
            }
        }

        return suspiciousDiagnostics >= 3;
    }

}
