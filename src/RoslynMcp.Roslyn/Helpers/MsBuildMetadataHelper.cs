using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Locates and reads MSBuild metadata files (<c>Directory.Packages.props</c>,
/// <c>Directory.Build.props</c>) by walking the directory hierarchy.
/// </summary>
internal static class MsBuildMetadataHelper
{
    /// <summary>
    /// Evaluates <paramref name="projectFilePath"/> with MSBuild in a throwaway
    /// <see cref="ProjectCollection"/> and returns the evaluated value of each requested property
    /// (empty string when unset), in the order of <paramref name="propertyNames"/>. Evaluation
    /// failures propagate as authored public refusals containing only the error code or failure
    /// type; captured property values and raw exception chains never leave this boundary.
    /// </summary>
    public static string[] EvaluateProperties(string projectFilePath, IReadOnlyDictionary<string, string> globals, params string[] propertyNames)
    {
        MsBuildInitializer.EnsureInitialized();
        return EvaluatePropertiesCore(projectFilePath, globals, propertyNames);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] EvaluatePropertiesCore(string path, IReadOnlyDictionary<string, string> globals, string[] names) =>
        EvaluateCore(path, globals, project => names.Select(project.GetPropertyValue).ToArray());

    internal sealed record PackageExpectation(string Id, string? RequestedVersion, bool UsesCentralVersion);
    internal sealed record RestoreInputs(string? AssetsPath, string? ExtensionsPath, IReadOnlyList<PackageExpectation> Packages, string TargetFramework, string TargetFrameworks, string RuntimeIdentifier, bool IsTestProject, string TargetFrameworkMoniker, bool IsTestingPlatformApplication, bool EnableMSTestRunner,
        bool EnableNUnitRunner, bool UseMicrosoftTestingPlatformRunner, bool UseVSTest, string OutputType);

    public static RestoreInputs EvaluateRestoreInputs(string projectFilePath, IReadOnlyDictionary<string, string> globals)
    {
        MsBuildInitializer.EnsureInitialized();
        return EvaluateRestoreInputsCore(projectFilePath, globals);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RestoreInputs EvaluateRestoreInputsCore(string projectFilePath, IReadOnlyDictionary<string, string> globals) =>
        EvaluateCore(projectFilePath, globals, project => ReadRestoreInputs(projectFilePath, project.GetPropertyValue,
            name => project.GetItems(name).Select(item => new EvaluatedItem(item.EvaluatedInclude, item.GetMetadataValue))));

    internal sealed record CompilerDeclaration(string Kind, string Path, string DefiningProject, string Include);
    internal sealed record CompilerDeclarations(IReadOnlyList<CompilerDeclaration> Items, IReadOnlyList<string> Imports,
        string? ContentPreprocessorDirectory);

    internal static CompilerDeclarations EvaluateCompilerDeclarations(string path, IReadOnlyDictionary<string, string> globals)
    {
        MsBuildInitializer.EnsureInitialized();
        return EvaluateCompilerDeclarationsCore(path, globals);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CompilerDeclarations EvaluateCompilerDeclarationsCore(string path, IReadOnlyDictionary<string, string> globals) =>
        EvaluateCore(path, globals, project =>
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var items = new List<CompilerDeclaration>();
            foreach (var kind in new[] { "Reference", "Analyzer", "Compile", "AdditionalFiles", "EditorConfigFiles" })
                foreach (var item in project.GetItems(kind))
                {
                    var value = kind == "Reference" ? item.GetMetadataValue("HintPath") : item.EvaluatedInclude;
                    var unresolved = string.IsNullOrWhiteSpace(value) || value.Contains("$(", StringComparison.Ordinal);
                    if (unresolved && kind != "Reference") continue;
                    var coordinate = unresolved ? "" : kind == "Reference"
                        ? Path.GetFullPath(value, directory) : item.GetMetadataValue("FullPath");
                    items.Add(new(kind, coordinate, item.Xml.ContainingProject.FullPath,
                        item.EvaluatedInclude));
                }
            var preprocess = project.GetPropertyValue("ContentPreprocessorOutputDirectory");
            return new CompilerDeclarations(items, project.Imports.Select(import => import.ImportedProject.FullPath)
                .Distinct(FileSystemPath.Comparer).ToArray(),
                string.IsNullOrWhiteSpace(preprocess) ? null : Path.GetFullPath(preprocess, directory));
        });

    internal sealed record EvaluatedItem(string Include, Func<string, string> Metadata);

    internal static RestoreInputs ReadRestoreInputs(string projectFilePath, Func<string, string> property,
        Func<string, IEnumerable<EvaluatedItem>> items)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
        var extensions = property("MSBuildProjectExtensionsPath");
        var extensionsPath = string.IsNullOrWhiteSpace(extensions) ? null
            : Path.TrimEndingDirectorySeparator(NativeItemPath.FullPath(extensions, directory));
        string? assets = property("ProjectAssetsFile");
        if (string.IsNullOrWhiteSpace(assets))
        {
            assets = extensionsPath is null ? null : Path.Combine(extensionsPath, "project.assets.json");
        }
        var central = items("PackageVersion").GroupBy(item => item.Include, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Metadata("Version"), StringComparer.OrdinalIgnoreCase);
        var managed = property("ManagePackageVersionsCentrally").Equals("true", StringComparison.OrdinalIgnoreCase);
        var packages = items("PackageReference").Select(item =>
        {
            string? version = item.Metadata("VersionOverride");
            if (string.IsNullOrWhiteSpace(version)) version = item.Metadata("Version");
            var usesCentral = string.IsNullOrWhiteSpace(version) && managed;
            if (usesCentral) central.TryGetValue(item.Include, out version);
            return new PackageExpectation(item.Include, string.IsNullOrWhiteSpace(version) ? null : version, usesCentral);
        }).ToArray();
        return new RestoreInputs(string.IsNullOrWhiteSpace(assets) ? null : Path.GetFullPath(assets, directory),
            extensionsPath, packages, property("TargetFramework"), property("TargetFrameworks"),
            property("RuntimeIdentifier"), property("IsTestProject").Equals("true", StringComparison.OrdinalIgnoreCase), property("TargetFrameworkMoniker"),
            property("IsTestingPlatformApplication").Equals("true", StringComparison.OrdinalIgnoreCase),
            property("EnableMSTestRunner").Equals("true", StringComparison.OrdinalIgnoreCase),
            property("EnableNUnitRunner").Equals("true", StringComparison.OrdinalIgnoreCase),
            property("UseMicrosoftTestingPlatformRunner").Equals("true", StringComparison.OrdinalIgnoreCase),
            property("UseVSTest").Equals("true", StringComparison.OrdinalIgnoreCase), property("OutputType"));
    }

    public static IReadOnlyList<RestoreInputs> EvaluateProjectContexts(string path, IReadOnlyDictionary<string, string> globals) =>
        RoslynMcp.Roslyn.Services.EvaluatedBuildGraph.ActiveEntries(
            RoslynMcp.Roslyn.Services.EvaluatedBuildGraph.FromRoots([path], globals,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None))
            .Select(node => node.Inputs).ToArray();

    public static string? ResolveAssetsPath(string projectFilePath, IReadOnlyDictionary<string, string> globals) =>
        EvaluateRestoreInputs(projectFilePath, globals).AssetsPath;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TResult EvaluateCore<TResult>(string projectFilePath, IReadOnlyDictionary<string, string> globals, Func<Project, TResult> read)
    {
        using var collection = new ProjectCollection();
        try
        {
            var project = collection.LoadProject(projectFilePath, globals.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), toolsVersion: null);
            return read(project);
        }
        catch (InvalidProjectFileException exception)
        {
            throw new RoslynMcp.Core.Services.PublicInvalidOperationException("MSBuild property evaluation failed (" + exception.ErrorCode + ").");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new RoslynMcp.Core.Services.PublicInvalidOperationException("MSBuild property evaluation failed (" + exception.GetType().Name + ").");
        }
        finally { collection.UnloadAllProjects(); }
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
