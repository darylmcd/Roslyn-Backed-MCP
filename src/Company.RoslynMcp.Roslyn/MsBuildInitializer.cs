using Microsoft.Build.Locator;

namespace Company.RoslynMcp.Roslyn;

public static class MsBuildInitializer
{
    private static bool _initialized;
    private static readonly object _lock = new();

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
