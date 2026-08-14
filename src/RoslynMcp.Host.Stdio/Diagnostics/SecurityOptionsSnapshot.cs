using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// sanctioned-roots-empty-boundary-fails-silently-until-first-call: holds the
/// <see cref="SecurityOptions"/> bound at host startup so <c>server_info</c> can report the
/// filesystem-boundary state without injecting the options into every tool that wants them.
/// Mirrors <see cref="SurfaceRegistrationSnapshot"/>, which exists for the same reason.
/// <para>
/// Tool-call paths must tolerate <see cref="Value"/> being <see langword="null"/> — unit tests
/// that exercise tools without booting the full host never populate this snapshot. A null
/// snapshot means "unknown", NOT "unconfigured": reporting a fabricated fail-closed boundary
/// from an unbooted host would be the same silent-wrong-answer class this row exists to remove.
/// </para>
/// </summary>
public static class SecurityOptionsSnapshot
{
    private static volatile SecurityOptions? s_value;

    public static SecurityOptions? Value
    {
        get => s_value;
        set => s_value = value;
    }
}
