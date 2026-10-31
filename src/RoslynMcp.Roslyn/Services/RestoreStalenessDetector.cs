using System.Collections.Immutable;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Detects whether a loaded workspace is stale with respect to <c>dotnet restore</c> — both the
/// in-flight-restore race (concurrent out-of-process restore mutating <c>obj/</c> artefacts while
/// MSBuild opens the solution) and the after-the-fact drift check (installed package versions in
/// <c>project.assets.json</c> no longer matching the csproj's requested versions). Extracted from
/// <see cref="WorkspaceManager.LoadIntoSessionAsync"/> — workspace-manager-decompose-restore-and-
/// analyzer-subsystems — as a zero-external-caller, purely code-moved collaborator.
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
    /// Enumerates <c>obj/project.assets.json</c> and <c>obj/*.dgspec.json</c> under the
    /// workspace root, polls their <see cref="File.GetLastWriteTimeUtc"/>, and returns once
    /// every file's mtime has been stable for <see cref="RestoreRaceStableWindowMs"/> ms — or
    /// the <paramref name="restoreRaceWaitMs"/> cap fires. No-op when the cap is zero, when no
    /// artefacts exist (typical on a pristine checkout before first build), or when all
    /// artefacts are already stable on the first sample (typical on a healthy load after a
    /// completed restore).
    /// </remarks>
    public virtual async Task WaitForStableRestoreArtifactsAsync(
        string fullPath,
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
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return;
        }

        // Enumerate each project's obj/ directory artefacts once up-front and track their
        // timestamps in a small dictionary. EnumerateFiles is recursive but bounded by the
        // solution's own directory tree, so on real solutions this is a few dozen tiny stats.
        var artefacts = EnumerateRestoreArtefacts(rootDirectory);
        if (artefacts.Count == 0)
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(capMs);
        var lastSnapshot = new Dictionary<string, DateTime>(artefacts.Count, StringComparer.OrdinalIgnoreCase);
        var stableSince = new Dictionary<string, DateTime>(artefacts.Count, StringComparer.OrdinalIgnoreCase);

        // Seed the snapshot. A file that does not exist yet is tracked as DateTime.MinValue
        // so the stability check catches the "appears mid-poll" case (restore creating the
        // first project.assets.json while we race it).
        var seedNow = DateTime.UtcNow;
        foreach (var path in artefacts)
        {
            lastSnapshot[path] = SafeGetLastWriteTimeUtc(path);
            stableSince[path] = seedNow;
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var allStable = true;

            foreach (var path in artefacts)
            {
                var currentMtime = SafeGetLastWriteTimeUtc(path);
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
                    fullPath);
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

    private static List<string> EnumerateRestoreArtefacts(string rootDirectory)
    {
        // Enumerate every obj/ directory under the solution root. EnumerateDirectories with
        // SearchOption.AllDirectories is bounded by the solution tree; a typical solution
        // has one obj/ directory per project. We then look only for the two files Roslyn /
        // MSBuild actually consume during OpenSolutionAsync — project.assets.json and the
        // project's <Project>.dgspec.json — so deep nested Debug/Release/TFM subdirectories
        // do not inflate the probe set.
        var results = new List<string>();
        try
        {
            foreach (var objDir in Directory.EnumerateDirectories(rootDirectory, "obj", SearchOption.AllDirectories))
            {
                try
                {
                    var assetsPath = Path.Combine(objDir, "project.assets.json");
                    if (File.Exists(assetsPath))
                    {
                        results.Add(assetsPath);
                    }

                    foreach (var dgspec in Directory.EnumerateFiles(objDir, "*.dgspec.json", SearchOption.TopDirectoryOnly))
                    {
                        results.Add(dgspec);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Tolerate transient IO errors (a concurrent restore may delete/recreate
                    // subdirectories). The probe is best-effort; skip and continue.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot enumerate at all, skip the wait entirely.
        }

        return results;
    }

    private static DateTime SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            // File.Exists check collapses the "file deleted mid-probe" case into MinValue,
            // which the caller treats as a change in the next sample (restore rewrote it).
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    public virtual bool DetectRestoreRequired(ImmutableArray<ProjectStatusDto> projects, IReadOnlyDictionary<string, string> globalProperties, ILogger logger)
    {
        foreach (var project in projects)
        {
            if (IsRestoreRequired(project.FilePath, globalProperties, logger))
            {
                return true;
            }
        }

        return false;
    }

    public virtual bool IsRestoreRequired(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        Dictionary<string, PackageExpectation> expectedPackages;
        try
        {
            expectedPackages = CollectExpectedPackages(projectFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger.LogDebug("restore-drift metadata could not be read ({FailureType}); package expectations are unknown.",
                ex.GetType().Name);
            return false;
        }

        if (expectedPackages.Count == 0)
        {
            return false;
        }

        try
        {
            var assets = LoadAssetsPackageVersions(projectFilePath, globalProperties);
            if (assets is null)
            {
                logger.LogDebug("restore-drift assets are missing or have an unsupported structure; restore is required.");
                return true;
            }

            foreach (var (packageId, expectation) in expectedPackages)
            {
                if (!assets.Value.PackageVersions.TryGetValue(packageId, out var assetVersions))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(expectation.RequestedVersion) &&
                    !PackageVersionMatches(expectation.RequestedVersion!, assetVersions))
                {
                    return true;
                }

                if (expectation.UsesCentralVersion)
                {
                    if (string.IsNullOrWhiteSpace(expectation.RequestedVersion) ||
                        !assets.Value.CentralPackageVersions.TryGetValue(packageId, out var centralVersions) ||
                        !PackageVersionMatches(expectation.RequestedVersion!, centralVersions))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Known package expectations cannot be verified against unusable assets. Preserve
            // the restore-required signal without retaining file content, paths, or exception text.
            logger.LogDebug("restore-drift assets could not be read ({FailureType}); restore is required.",
                ex.GetType().Name);
            return true;
        }

        return false;
    }

    private static Dictionary<string, PackageExpectation> CollectExpectedPackages(string projectFilePath)
    {
        var expected = new Dictionary<string, PackageExpectation>(StringComparer.OrdinalIgnoreCase);
        var centralPackages = LoadCentralPackageVersions(MsBuildMetadataHelper.FindDirectoryPackagesProps(projectFilePath));

        foreach (var documentPath in EnumeratePackageReferenceDocuments(projectFilePath))
        {
            XDocument document;
            try
            {
                document = XmlFileLoader.Load(documentPath, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var element in document
                         .Descendants()
                         .Where(candidate => string.Equals(candidate.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)))
            {
                var packageId = GetXmlValue(element, "Include") ?? GetXmlValue(element, "Update");
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                var explicitVersion =
                    GetXmlValue(element, "VersionOverride") ??
                    GetChildValue(element, "VersionOverride") ??
                    GetXmlValue(element, "Version") ??
                    GetChildValue(element, "Version");

                centralPackages.TryGetValue(packageId, out var centralVersion);
                var usesCentralVersion = string.IsNullOrWhiteSpace(explicitVersion) &&
                                         !string.IsNullOrWhiteSpace(centralVersion);
                expected[packageId] = new PackageExpectation(
                    usesCentralVersion ? centralVersion : explicitVersion,
                    usesCentralVersion);
            }
        }

        return expected;
    }

    private static IEnumerable<string> EnumeratePackageReferenceDocuments(string projectFilePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buildPropsPath = FindNearestFile(projectFilePath, "Directory.Build.props");
        if (!string.IsNullOrWhiteSpace(buildPropsPath) && seen.Add(buildPropsPath))
        {
            yield return buildPropsPath;
        }

        if (seen.Add(projectFilePath))
        {
            yield return projectFilePath;
        }

        var buildTargetsPath = FindNearestFile(projectFilePath, "Directory.Build.targets");
        if (!string.IsNullOrWhiteSpace(buildTargetsPath) && seen.Add(buildTargetsPath))
        {
            yield return buildTargetsPath;
        }
    }

    private static string? FindNearestFile(string path, string fileName)
    {
        var directory = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static Dictionary<string, string> LoadCentralPackageVersions(string? packagesPropsPath)
    {
        var centralPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(packagesPropsPath) || !File.Exists(packagesPropsPath))
        {
            return centralPackages;
        }

        XDocument document;
        try
        {
            document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return centralPackages;
        }

        foreach (var element in document
                     .Descendants()
                     .Where(candidate => string.Equals(candidate.Name.LocalName, "PackageVersion", StringComparison.OrdinalIgnoreCase)))
        {
            var packageId = GetXmlValue(element, "Include");
            var version = GetXmlValue(element, "Version") ?? GetChildValue(element, "Version");
            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version))
            {
                centralPackages[packageId] = version;
            }
        }

        return centralPackages;
    }

    /// <summary>
    /// workspace-restore-packages-path: reads <c>project.restore.packagesPath</c> from the project's
    /// evaluated active assets file — the package folder the last restore wrote to (for
    /// example a worktree scratch folder). Returns <see langword="null"/> when the assets file, the
    /// property, or a non-empty string value is absent, or when the file cannot be read. This
    /// best-effort drift read does not authorize restore commands; the invocation planner uses
    /// <see cref="ReadRestorePackagesPathForInvocation"/> to refuse unreadable active assets.
    /// </summary>
    public static string? TryReadRestorePackagesPath(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties)
    {
        try
        {
            var assetsPath = ProjectMetadataParser.GetExistingAssetsPath(projectFilePath, globalProperties);
            if (assetsPath is null)
            {
                return null;
            }

            using var stream = File.OpenRead(assetsPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("project", out var project) &&
                project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("restore", out var restore) &&
                restore.ValueKind == JsonValueKind.Object &&
                restore.TryGetProperty("packagesPath", out var packagesPath) &&
                packagesPath.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(packagesPath.GetString()))
            {
                return packagesPath.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable assets file: treated as a first restore (no --packages override).
        }

        return null;
    }

    /// <summary>Read the active restore authority for command planning; only a missing file is a first restore.</summary>
    internal static string? ReadRestorePackagesPathForInvocation(string assetsPath)
    {
        try
        {
            // File.Exists also returns false on access failures, so it cannot establish absence.
            var attributes = File.GetAttributes(assetsPath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new IOException("The evaluated assets path is a directory.");
            }

            using var stream = File.OpenRead(assetsPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("project", out var project) ||
                project.ValueKind != JsonValueKind.Object ||
                !project.TryGetProperty("restore", out var restore) ||
                restore.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("The active assets file has no restore object.");
            }

            if (!restore.TryGetProperty("packagesPath", out var packagesPath)) return null;
            if (packagesPath.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("The active assets file has an invalid packagesPath.");
            }

            var value = packagesPath.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PublicInvalidOperationException(
                "The active restore assets file could not be read. Restore it in its evaluated location and retry.",
                InvalidOperationErrors.Internal("Could not read the active restore assets file.", ex));
        }
    }

    /// <summary>
    /// workspace-load-missing-assets-auto-restore: true when the project declares at least one
    /// <c>PackageReference</c> but has no <c>project.assets.json</c> at all — the "never restored"
    /// state, distinct from version drift (which <see cref="IsRestoreRequired"/> also reports). The
    /// assets file is located where MSBuild would write it, so <c>UseArtifactsOutput</c> and a
    /// custom <c>BaseIntermediateOutputPath</c>/<c>MSBuildProjectExtensionsPath</c> are not
    /// misread as missing. Known package expectations without a locatable active assets file
    /// are reported as missing, including unevaluable layouts. Unreadable package expectations
    /// are reported as not-missing and logged at debug.
    /// </summary>
    public static bool HasMissingAssets(string projectFilePath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        try
        {
            return CollectExpectedPackages(projectFilePath).Count > 0 &&
                   ProjectMetadataParser.GetExistingAssetsPath(projectFilePath, MsBuildMetadataHelper.DefaultGlobalProperties) is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger.LogDebug(ex, "missing-assets detection skipped for '{ProjectFilePath}'", projectFilePath);
            return false;
        }
    }

    private static (Dictionary<string, HashSet<string>> PackageVersions, Dictionary<string, HashSet<string>> CentralPackageVersions)? LoadAssetsPackageVersions(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties)
    {
        var assetsPath = ProjectMetadataParser.GetExistingAssetsPath(projectFilePath, globalProperties);
        if (assetsPath is null)
        {
            return null;
        }

        using var stream = File.OpenRead(assetsPath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("project", out var project) ||
            project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var packageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var centralPackageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                if (dependencies.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    if (!dependency.Value.TryGetProperty("version", out var versionElement))
                    {
                        continue;
                    }

                    if (versionElement.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    AddVersion(packageVersions, dependency.Name, versionElement.GetString());
                }
            }

            if (framework.Value.TryGetProperty("centralPackageVersions", out var centralVersions))
            {
                if (centralVersions.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var centralVersion in centralVersions.EnumerateObject())
                {
                    if (centralVersion.Value.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

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
        var expected = expectedVersion.Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (actualVersions.Contains(expected))
        {
            return true;
        }

        if (expected.StartsWith("[", StringComparison.Ordinal) || expected.StartsWith("(", StringComparison.Ordinal))
        {
            return false;
        }

        return actualVersions.Contains($"[{expected}, )");
    }

    private static string? GetXmlValue(XElement element, string localName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
    }

    private static string? GetChildValue(XElement element, string localName)
    {
        return element.Elements().FirstOrDefault(child =>
            string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
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

    private readonly record struct PackageExpectation(string? RequestedVersion, bool UsesCentralVersion);
}
