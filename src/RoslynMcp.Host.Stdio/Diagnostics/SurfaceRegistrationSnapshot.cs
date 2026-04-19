namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Holds the <see cref="StartupDiagnostics.SurfaceRegistrationReport"/> captured at
/// <c>host.Build()</c> time so <c>server_info</c> can echo runtime-registered surface
/// counts to clients without re-injecting the <see cref="ModelContextProtocol.Server.McpServer"/>
/// into every tool that wants to report them.
/// <para>
/// Tool-call paths must tolerate <see cref="Value"/> being <see langword="null"/> —
/// unit tests that exercise tools without booting the full host never populate this
/// snapshot.
/// </para>
/// </summary>
public static class SurfaceRegistrationSnapshot
{
    private static volatile StartupDiagnostics.SurfaceRegistrationReport? s_value;

    public static StartupDiagnostics.SurfaceRegistrationReport? Value
    {
        get => s_value;
        set => s_value = value;
    }
}
