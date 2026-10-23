using System.Collections.Immutable;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Exact active assets entries; a package directory alone never certifies a compiler input.</summary>
internal sealed record RestoredPackageManifest(
    ImmutableHashSet<string> Compile,
    ImmutableHashSet<string> AnalyzerCandidates,
    ImmutableHashSet<string> PackageFiles,
    ImmutableDictionary<string, string> ContentActions,
    ImmutableArray<RestoredProjectLibrary> TransitiveProjects)
{
    internal static RestoredPackageManifest Empty { get; } = new(
        ImmutableHashSet.Create<string>(FileSystemPath.Comparer),
        ImmutableHashSet.Create<string>(FileSystemPath.Comparer),
        ImmutableHashSet.Create<string>(FileSystemPath.Comparer),
        ImmutableDictionary.Create<string, string>(FileSystemPath.Comparer), []);

    internal static RestoredPackageManifest Read(JsonElement root, MsBuildMetadataHelper.RestoreInputs inputs,
        string? preprocessDirectory, string language, InstalledPackageAuthority installed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        try { return ReadCore(root, inputs, preprocessDirectory, language, installed, ct); }
        catch (Exception exception) when (exception is ArgumentException
            || exception is InvalidOperationException and not PublicInvalidOperationException)
        { throw Refusal(); }
    }

    private static RestoredPackageManifest ReadCore(JsonElement root, MsBuildMetadataHelper.RestoreInputs inputs,
        string? preprocessDirectory, string language, InstalledPackageAuthority installed, CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Object) throw Refusal();
        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object) throw Refusal();
        if (!ProjectMetadataParser.TryGetAssetsTarget(root, inputs, includeRuntimeIdentifier: false, out var compileTarget)
            || !ProjectMetadataParser.TryGetAssetsTarget(root, inputs, includeRuntimeIdentifier: true, out var runtimeTarget)) throw Refusal();
        var compile = Empty.Compile.ToBuilder();
        var analyzers = Empty.AnalyzerCandidates.ToBuilder();
        var files = Empty.PackageFiles.ToBuilder();
        var actions = Empty.ContentActions.ToBuilder();
        var languageCode = language == "Visual Basic" ? "vb" : "cs";
        var packageNames = compileTarget.EnumerateObject().Where(item => IsPackage(item.Value)).Select(item => item.Name)
            .Concat(runtimeTarget.EnumerateObject().Where(item => IsPackage(item.Value)).Select(item => item.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var packageName in packageNames)
        {
            ct.ThrowIfCancellationRequested();
            if (!libraries.TryGetProperty(packageName, out var library) || !IsPackage(library)
                || !library.TryGetProperty("path", out var relative) || relative.ValueKind != JsonValueKind.String) throw Refusal();
            var roots = installed.Installations(packageName);
            if (roots.Length == 0) throw Refusal();
            foreach (var packageRoot in roots)
            {
                if (compileTarget.TryGetProperty(packageName, out var compileLibrary) && compileLibrary.TryGetProperty("compile", out var assemblies))
                    foreach (var asset in assemblies.EnumerateObject())
                        if (!asset.Name.EndsWith("/_._", StringComparison.Ordinal)) compile.Add(CertifiedChild(packageRoot, asset.Name));
                if (library.TryGetProperty("files", out var packageFiles))
                    foreach (var entry in packageFiles.EnumerateArray())
                    {
                        var name = entry.GetString() ?? throw Refusal();
                        var path = CertifiedChild(packageRoot, name);
                        files.Add(path);
                        var normalized = name.Replace('\\', '/');
                        // This is a candidate inventory, not the SDK's analyzer selection algorithm.
                        // The native evaluated compiler supplies the selected Roslyn-version subset.
                        if (normalized.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            && !normalized.Contains(languageCode == "cs" ? "/vb/" : "/cs/", StringComparison.OrdinalIgnoreCase)
                            && !normalized.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) analyzers.Add(path);
                    }
                if (runtimeTarget.TryGetProperty(packageName, out var runtimeLibrary)
                    && runtimeLibrary.TryGetProperty("contentFiles", out var content))
                    foreach (var entry in content.EnumerateObject())
                    {
                        var metadata = entry.Value;
                        var code = String(metadata, "codeLanguage");
                        if (code.Length > 0 && code != "any" && !code.Equals(languageCode, StringComparison.OrdinalIgnoreCase)) continue;
                        var action = String(metadata, "buildAction");
                        var output = String(metadata, "ppOutputPath");
                        var path = CertifiedChild(packageRoot, entry.Name);
                        if (output.Length > 0)
                        {
                            if (preprocessDirectory is null) throw Refusal();
                            var separator = packageName.LastIndexOf('/');
                            if (separator < 1) throw Refusal();
                            path = Child(preprocessDirectory, packageName[..separator] + "/" + packageName[(separator + 1)..] + "/" + output);
                        }
                        actions[path] = action;
                    }
            }
        }
        return new(compile.ToImmutable(), analyzers.ToImmutable(), files.ToImmutable(), actions.ToImmutable(),
            ReadTransitiveProjects(root, libraries, runtimeTarget, inputs, ct));

        string CertifiedChild(string directory, string relativePath)
        {
            var path = Child(directory, relativePath);
            if (!installed.Files.Contains(path, FileSystemPath.Comparer)) throw Refusal();
            return path;
        }
    }

    // SDK floor 10.0.400 ResolvePackageAssets.WriteTransitiveProjectReferences consumes the runtime
    // target, excludes applicable direct dependencies and compile placeholders, then takes
    // the project path from libraries[].msbuildProject. This is not graph reachability alone.
    // https://github.com/dotnet/sdk/blob/v10.0.400/src/Tasks/Microsoft.NET.Build.Tasks/LockFileExtensions.cs#L215
    internal static ImmutableArray<RestoredProjectLibrary> ReadProjectLibraries(JsonElement root,
        MsBuildMetadataHelper.RestoreInputs inputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        try
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("libraries", out var libraries)
                || libraries.ValueKind != JsonValueKind.Object
                || !ProjectMetadataParser.TryGetAssetsTarget(root, inputs, true, out var target)) throw Refusal();
            return ReadTransitiveProjects(root, libraries, target, inputs, ct);
        }
        catch (Exception failure) when (failure is ArgumentException || failure is InvalidOperationException and not PublicInvalidOperationException)
        { throw Refusal(); }
    }

    private static ImmutableArray<RestoredProjectLibrary> ReadTransitiveProjects(JsonElement root, JsonElement libraries,
        JsonElement runtimeTarget, MsBuildMetadataHelper.RestoreInputs inputs, CancellationToken ct)
    {
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = string.IsNullOrWhiteSpace(inputs.TargetFramework) ? inputs.TargetFrameworkMoniker : inputs.TargetFramework;
        if (root.TryGetProperty("projectFileDependencyGroups", out var groups))
        {
            if (groups.ValueKind != JsonValueKind.Object) throw Refusal();
            foreach (var group in groups.EnumerateObject())
            {
                if (!SameFramework(group.Name, selected)) continue;
                foreach (var item in group.Value.EnumerateArray())
                {
                    var value = item.GetString() ?? throw Refusal();
                    var separator = value.TakeWhile(character => !char.IsWhiteSpace(character)).Count();
                    direct.Add(value[..separator]);
                }
            }
        }
        var result = ImmutableArray.CreateBuilder<RestoredProjectLibrary>();
        foreach (var item in runtimeTarget.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            if (!String(item.Value, "type").Equals("project", StringComparison.Ordinal)) continue;
            var separator = item.Name.LastIndexOf('/');
            if (separator < 1) throw Refusal();
            if (direct.Contains(item.Name[..separator])) continue;
            if (item.Value.TryGetProperty("compile", out var compile) && compile.EnumerateObject()
                .Any(asset => asset.Name.Replace('\\', '/').Split('/').Last() == "_._")) continue;
            if (!libraries.TryGetProperty(item.Name, out var library) || String(library, "type") != "project") throw Refusal();
            var project = String(library, "msbuildProject");
            if (string.IsNullOrWhiteSpace(project)) throw Refusal();
            result.Add(new(project.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar),
                String(item.Value, "framework")));
        }
        return result.ToImmutable();
    }

    internal static bool SameFramework(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Equals(right, StringComparison.Ordinal)) return true;
        var first = ParseFramework(left.Split('/')[0]);
        var second = ParseFramework(right);
        return !first.IsUnsupported && !second.IsUnsupported && first.DotNetFrameworkName == second.DotNetFrameworkName;
    }

    internal static bool SameTargetFramework(string left, string right)
    {
        var first = ParseFramework(left);
        var second = ParseFramework(right);
        return !first.IsUnsupported && !second.IsUnsupported && first.Equals(second);
    }

    private static NuGet.Frameworks.NuGetFramework ParseFramework(string value) => value.Contains(',')
        ? NuGet.Frameworks.NuGetFramework.Parse(value) : NuGet.Frameworks.NuGetFramework.ParseFolder(value);

    private static string String(JsonElement value, string name) => value.TryGetProperty(name, out var result)
        && result.ValueKind == JsonValueKind.String ? result.GetString()! : "";
    private static bool IsPackage(JsonElement value) => value.ValueKind == JsonValueKind.Object
        && String(value, "type").Equals("package", StringComparison.OrdinalIgnoreCase);
    private static string Child(string root, string relative)
    {
        if (!Path.IsPathFullyQualified(root) || Path.IsPathRooted(relative)) throw Refusal();
        var result = Path.GetFullPath(relative, root);
        if (!FileSystemPath.IsStrictDescendant(root, result)) throw Refusal();
        return result;
    }
    private static PublicInvalidOperationException Refusal() => new("Restore assets do not establish one complete active compiler-input provenance.");
}

internal sealed record RestoredProjectLibrary(string ProjectPath, string TargetFramework);
