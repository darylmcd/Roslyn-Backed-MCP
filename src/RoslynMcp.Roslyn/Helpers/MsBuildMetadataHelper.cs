using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Locates and reads MSBuild metadata files (<c>Directory.Packages.props</c>,
/// <c>Directory.Build.props</c>) by walking the directory hierarchy.
/// </summary>
internal static class MsBuildMetadataHelper
{
    // Root capture and missing-assets readers accompany commands that restore with default settings.
    public static IReadOnlyDictionary<string, string> DefaultGlobalProperties { get; } = ImmutableDictionary<string, string>.Empty;

    public static IReadOnlyDictionary<string, string> GetWorkspaceGlobalProperties(Microsoft.CodeAnalysis.Workspace workspace) =>
        workspace is MSBuildWorkspace msbuildWorkspace ? msbuildWorkspace.Properties : DefaultGlobalProperties;

    /// <summary>
    /// Evaluates <paramref name="projectFilePath"/> with MSBuild in a throwaway
    /// <see cref="ProjectCollection"/> using the operation's explicit <paramref name="globalProperties"/>,
    /// and returns the evaluated value of each requested property
    /// (empty string when unset), in the order of <paramref name="propertyNames"/>. Evaluation
    /// failures propagate (an <c>InvalidProjectFileException</c> as <see cref="InvalidOperationException"/>,
    /// I/O as-is) so each caller picks its own degradation policy.
    /// </summary>
    public static string[] EvaluateProperties(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties, params string[] propertyNames)
    {
        // The MSBuild types live in a separate non-inlined method: the JIT resolves
        // Microsoft.Build when it compiles a method that mentions those types, which must happen
        // only after EnsureInitialized has registered the MSBuild assembly resolver.
        MsBuildInitializer.EnsureInitialized();
        return EvaluatePropertiesCore(projectFilePath, globalProperties, propertyNames);
    }

    internal static bool IsExpectedEvaluationFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException ||
        exception.GetType() == typeof(InvalidOperationException) &&
        exception.InnerException is not null && IsInvalidProjectFailure(exception.InnerException);

    // EvaluateProperties registers the resolver before evaluation can produce this wrapper.
    // Keep the Microsoft.Build type out of callers' JIT compilation, just like the evaluator.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsInvalidProjectFailure(Exception exception) => exception is InvalidProjectFileException;

    /// <summary>
    /// One MSBuild project collection for the calling load projection. A second request for the
    /// same project file and the same global properties reuses the project already loaded into
    /// that collection. The collection is unloaded and disposed when the scope ends, including
    /// when the projection fails. Callers outside the scope keep a throwaway collection.
    /// </summary>
    internal static IDisposable BeginSharedEvaluation(IReadOnlyDictionary<string, string> globalProperties)
    {
        MsBuildInitializer.EnsureInitialized();
        return EnterSharedEvaluation(globalProperties);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EvaluationScope EnterSharedEvaluation(IReadOnlyDictionary<string, string> globalProperties)
    {
        var scope = new EvaluationScope(globalProperties, SharedEvaluation.Value);
        SharedEvaluation.Value = scope;
        return scope;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] EvaluatePropertiesCore(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties, string[] propertyNames)
    {
        var shared = SharedEvaluation.Value;
        if (shared is not null && shared.Matches(globalProperties))
        {
            return shared.Evaluate(projectFilePath, globalProperties, propertyNames);
        }

        using var projectCollection = new ProjectCollection(new Dictionary<string, string>(globalProperties));
        try
        {
            return ReadProperties(projectCollection, projectFilePath, globalProperties, propertyNames);
        }
        catch (InvalidProjectFileException ex)
        {
            // Surfaced as InvalidOperationException so callers never name a Microsoft.Build type
            // in a catch clause: the JIT would resolve it before the MSBuild resolver is registered.
            throw InvalidOperationErrors.Internal($"MSBuild could not evaluate '{projectFilePath}': {ex.Message}", ex);
        }
        finally
        {
            projectCollection.UnloadAllProjects();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] ReadProperties(
        ProjectCollection projectCollection,
        string projectFilePath,
        IReadOnlyDictionary<string, string> globalProperties,
        string[] propertyNames)
    {
        var loadedBefore = projectCollection.LoadedProjects.Count;
        var evaluated = projectCollection.LoadProject(projectFilePath);
        if (projectCollection.LoadedProjects.Count > loadedBefore)
        {
            RecordIfEnabled(projectFilePath, globalProperties, propertyNames);
        }

        return propertyNames.Select(evaluated.GetPropertyValue).ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadOne(ProjectCollection projectCollection, string projectFilePath)
    {
        foreach (var project in projectCollection.GetLoadedProjects(projectFilePath).ToArray())
        {
            projectCollection.UnloadProject(project);
        }
    }

    private static readonly AsyncLocal<EvaluationScope?> SharedEvaluation = new();

    private static int _liveSharedEvaluations;

    internal static int LiveSharedEvaluations => Volatile.Read(ref _liveSharedEvaluations);

    private sealed class EvaluationScope : IDisposable
    {
        private readonly Dictionary<string, string> _globals;
        private readonly EvaluationScope? _previous;
        private readonly ProjectCollection _collection;
        private bool _disposed;

        public EvaluationScope(IReadOnlyDictionary<string, string> globalProperties, EvaluationScope? previous)
        {
            _globals = new Dictionary<string, string>(globalProperties, StringComparer.Ordinal);
            _previous = previous;
            _collection = new ProjectCollection(new Dictionary<string, string>(_globals, StringComparer.Ordinal));
            Interlocked.Increment(ref _liveSharedEvaluations);
        }

        public bool Matches(IReadOnlyDictionary<string, string> globalProperties)
        {
            if (globalProperties.Count != _globals.Count)
            {
                return false;
            }

            foreach (var pair in globalProperties)
            {
                if (!_globals.TryGetValue(pair.Key, out var value) ||
                    !string.Equals(value, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public string[] Evaluate(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties, string[] propertyNames)
        {
            try
            {
                return ReadProperties(_collection, projectFilePath, globalProperties, propertyNames);
            }
            catch (InvalidProjectFileException ex)
            {
                UnloadOne(_collection, projectFilePath);
                throw InvalidOperationErrors.Internal($"MSBuild could not evaluate '{projectFilePath}': {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(SharedEvaluation.Value, this))
            {
                SharedEvaluation.Value = _previous;
            }

            _collection.UnloadAllProjects();
            _collection.Dispose();
            Interlocked.Decrement(ref _liveSharedEvaluations);
        }
    }

    /// <summary>
    /// Searches the directory tree from <paramref name="loadedPath"/> upward for
    /// the nearest <c>Directory.Packages.props</c> file.
    /// </summary>
    /// <returns>The absolute path to the file, or <see langword="null"/> if not found.</returns>
    public static string? FindDirectoryPackagesProps(string? loadedPath)
    {
        return FindNearestFile(loadedPath, "Directory.Packages.props");
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given <c>Directory.Packages.props</c> file has
    /// <c>ManagePackageVersionsCentrally</c> set to <c>true</c>.
    /// </summary>
    public static bool IsCentralPackageManagementEnabled(string packagesPropsPath)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return false;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        return string.Equals(
            document.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> if a <c>PackageVersion</c> element with
    /// <c>Include="<paramref name="packageId"/>"</c> exists in the given <c>Directory.Packages.props</c> file.
    /// </summary>
    public static bool ContainsCentralPackageVersion(string packagesPropsPath, string packageId)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return false;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        return document.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the <c>Version</c> attribute for a <c>PackageVersion</c> with matching <c>Include</c>, or <see langword="null"/>.
    /// </summary>
    public static string? TryGetCentralPackageVersion(string packagesPropsPath, string packageId)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return null;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        var element = document.Descendants("PackageVersion").FirstOrDefault(e =>
            string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        return element?.Attribute("Version")?.Value;
    }

    private static int _recordEvaluations;

    private static readonly List<EvaluationCall> _recordedEvaluations = new();

    internal readonly record struct EvaluationCall(string ProjectPath, string Properties, string GlobalProperties);

    internal static void BeginEvaluationRecording()
    {
        lock (_recordedEvaluations)
        {
            _recordedEvaluations.Clear();
            Volatile.Write(ref _recordEvaluations, 1);
        }
    }

    internal static EvaluationCall[] EndEvaluationRecording()
    {
        lock (_recordedEvaluations)
        {
            Volatile.Write(ref _recordEvaluations, 0);
            var copy = _recordedEvaluations.ToArray();
            _recordedEvaluations.Clear();
            return copy;
        }
    }

    private static void RecordIfEnabled(string projectFilePath, IReadOnlyDictionary<string, string> globalProperties, string[] propertyNames)
    {
        if (Volatile.Read(ref _recordEvaluations) == 0)
        {
            return;
        }

        var identity = string.Join(
            ";",
            globalProperties
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
        var properties = string.Join(",", propertyNames);
        lock (_recordedEvaluations)
        {
            if (Volatile.Read(ref _recordEvaluations) == 0)
            {
                return;
            }

            _recordedEvaluations.Add(new EvaluationCall(projectFilePath, properties, identity));
        }
    }

    private static string? FindNearestFile(string? loadedPath, string fileName)
    {
        var directory = ResolveDirectory(loadedPath);
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

    private static string? ResolveDirectory(string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            return null;
        }

        if (Directory.Exists(loadedPath))
        {
            return loadedPath;
        }

        if (File.Exists(loadedPath))
        {
            return Path.GetDirectoryName(loadedPath);
        }

        return Path.GetDirectoryName(loadedPath);
    }
}
