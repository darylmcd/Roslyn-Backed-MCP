using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio;

/// <summary>Fail-fast parsing for enumerated host environment options.</summary>
internal static class HostEnvironmentOptions
{
    public static StalenessPolicy ParseStalenessPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return StalenessPolicy.AutoReload;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto-reload" or "autoreload" => StalenessPolicy.AutoReload,
            "warn" => StalenessPolicy.Warn,
            "off" or "none" or "disabled" => StalenessPolicy.Off,
            _ => throw new ArgumentException(
                $"Invalid ROSLYNMCP_ON_STALE value '{value}'. Use 'auto-reload', 'warn', or 'off'.",
                nameof(value)),
        };
    }
}
