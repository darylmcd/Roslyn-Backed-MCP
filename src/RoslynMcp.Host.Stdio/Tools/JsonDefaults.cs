using System.Text.Json;

namespace RoslynMcp.Host.Stdio;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
