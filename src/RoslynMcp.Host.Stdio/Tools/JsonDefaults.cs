using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new CommandExecutionDtoJsonConverter(),
            new TestRunResultDtoJsonConverter(),
        }
    };
}
