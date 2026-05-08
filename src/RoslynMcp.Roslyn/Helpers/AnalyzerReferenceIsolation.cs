using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Roslyn.Helpers;

internal static class AnalyzerReferenceIsolation
{
    private static readonly FieldInfo? AssemblyLoaderField = typeof(AnalyzerFileReference)
        .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? LazyAssemblyField = typeof(AnalyzerFileReference)
        .GetField("_lazyAssembly", BindingFlags.Instance | BindingFlags.NonPublic);

    public static int RetargetFileReferencesToShadowLoader(
        Solution solution,
        string workspaceId,
        ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        if (AssemblyLoaderField is null)
        {
            logger.LogWarning("Could not locate AnalyzerFileReference loader field; analyzer references will use Roslyn's default loader.");
            return 0;
        }

        var shadowRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpAnalyzerShadow", workspaceId);
        var loaders = new Dictionary<string, ShadowCopyAnalyzerAssemblyLoader>(StringComparer.OrdinalIgnoreCase);
        var retargeted = 0;

        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.AnalyzerReferences.OfType<AnalyzerFileReference>())
            {
                if (!TryGetExistingFullPath(reference.FullPath, out var analyzerPath))
                {
                    continue;
                }

                if (LazyAssemblyField?.GetValue(reference) is not null)
                {
                    logger.LogWarning(
                        "Analyzer reference {AnalyzerPath} was already loaded before shadow-loader isolation could run.",
                        analyzerPath);
                    continue;
                }

                if (!loaders.TryGetValue(analyzerPath, out var loader))
                {
                    loader = ShadowCopyAnalyzerAssemblyLoader.Create(analyzerPath, shadowRoot, logger);
                    loaders[analyzerPath] = loader;
                }

                AssemblyLoaderField.SetValue(reference, loader);
                retargeted++;
            }
        }

        return retargeted;
    }

    private static bool TryGetExistingFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed class ShadowCopyAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly string _shadowRoot;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, string> _dependenciesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Assembly> _assembliesByIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _shadowCopiesByIdentity = new(StringComparer.OrdinalIgnoreCase);

        private ShadowCopyAnalyzerAssemblyLoader(string shadowRoot, ILogger logger)
        {
            _shadowRoot = shadowRoot;
            _logger = logger;
        }

        public static ShadowCopyAnalyzerAssemblyLoader Create(string analyzerPath, string shadowRoot, ILogger logger)
        {
            var loader = new ShadowCopyAnalyzerAssemblyLoader(shadowRoot, logger);
            loader.RegisterAnalyzerDirectory(analyzerPath);
            return loader;
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (!TryGetExistingFullPath(fullPath, out var normalized))
            {
                return;
            }

            var simpleName = TryGetAssemblySimpleName(normalized) ?? Path.GetFileNameWithoutExtension(normalized);
            if (!string.IsNullOrWhiteSpace(simpleName))
            {
                _dependenciesByName[simpleName] = normalized;
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            if (!TryGetExistingFullPath(fullPath, out var normalized))
            {
                throw new FileNotFoundException($"Analyzer assembly was not found: {fullPath}", fullPath);
            }

            AddDependencyLocation(normalized);
            var identity = BuildFileIdentity(normalized);
            return _assembliesByIdentity.GetOrAdd(identity, _ =>
            {
                var context = new ShadowAnalyzerLoadContext(this);
                return context.LoadFromAssemblyPath(ShadowCopy(normalized));
            });
        }

        private void RegisterAnalyzerDirectory(string analyzerPath)
        {
            AddDependencyLocation(analyzerPath);

            var directory = Path.GetDirectoryName(analyzerPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var candidate in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddDependencyLocation(candidate);
            }
        }

        private bool TryResolveDependency(AssemblyName assemblyName, out string dependencyPath)
        {
            dependencyPath = string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                return false;
            }

            if (!_dependenciesByName.TryGetValue(assemblyName.Name, out var foundPath))
            {
                return false;
            }

            dependencyPath = foundPath;
            return true;
        }

        private string ShadowCopy(string fullPath)
        {
            var identity = BuildFileIdentity(fullPath);
            return _shadowCopiesByIdentity.GetOrAdd(identity, _ =>
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
                var targetDirectory = Path.Combine(_shadowRoot, hash);
                Directory.CreateDirectory(targetDirectory);

                var targetPath = Path.Combine(targetDirectory, Path.GetFileName(fullPath));
                try
                {
                    File.Copy(fullPath, targetPath, overwrite: true);
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    // Another concurrent request populated the same shadow path.
                }

                return targetPath;
            });
        }

        private static string BuildFileIdentity(string fullPath)
        {
            var info = new FileInfo(fullPath);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        private static string? TryGetAssemblySimpleName(string fullPath)
        {
            try
            {
                return AssemblyName.GetAssemblyName(fullPath).Name;
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                return null;
            }
        }

        private Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), assemblyName))
                {
                    return loaded;
                }
            }

            if (!TryResolveDependency(assemblyName, out var dependencyPath))
            {
                return null;
            }

            try
            {
                return context.LoadFromAssemblyPath(ShadowCopy(dependencyPath));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
            {
                _logger.LogDebug(ex, "Failed to shadow-load analyzer dependency {DependencyPath}", dependencyPath);
                return null;
            }
        }

        private sealed class ShadowAnalyzerLoadContext : AssemblyLoadContext
        {
            private readonly ShadowCopyAnalyzerAssemblyLoader _loader;

            public ShadowAnalyzerLoadContext(ShadowCopyAnalyzerAssemblyLoader loader)
                : base(isCollectible: false)
            {
                _loader = loader;
            }

            protected override Assembly? Load(AssemblyName assemblyName) =>
                _loader.Resolve(this, assemblyName);
        }
    }
}
