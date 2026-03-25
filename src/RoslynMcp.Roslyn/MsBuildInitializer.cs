using Microsoft.Build.Locator;

namespace RoslynMcp.Roslyn;

/// <summary>
/// Ensures that MSBuild is registered with <see cref="MSBuildLocator"/> exactly once per process.
/// </summary>
/// <remarks>
/// <see cref="MSBuildLocator.RegisterDefaults"/> must be called before any Roslyn workspace types
/// attempt to load build logic. This class uses a double-checked lock to guarantee safe one-time
/// initialization in multi-threaded scenarios.
/// </remarks>
public static class MsBuildInitializer
{
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers the default MSBuild instance if it has not already been registered.
    /// Safe to call multiple times; subsequent calls are no-ops.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
            _initialized = true;
        }
    }
}
