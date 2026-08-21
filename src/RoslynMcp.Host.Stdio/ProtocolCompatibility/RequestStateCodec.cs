using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Host.Stdio.ProtocolCompatibility;

/// <summary>
/// Encodes the workspace identity that the filter resolved before a modern MRTR hand-off and
/// restores it on the client's retry. MCP request state is client-visible and echoed by the
/// client, so this carries only the same opaque workspace id callers may already submit; it is
/// not an authorization or secrecy boundary.
/// </summary>
internal static class RequestStateCodec
{
    private const string _workspacePrefix = "roslynmcp.workspace.v1:";

    internal static string? CaptureWorkspaceId(
        IDictionary<string, JsonElement>? arguments,
        string workspaceIdParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceIdParameterName);
        if (arguments is null ||
            !arguments.TryGetValue(workspaceIdParameterName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "N", out var workspaceId))
        {
            return null;
        }

        return _workspacePrefix + workspaceId.ToString("N");
    }

    internal static bool TryRestoreWorkspaceId(string? requestState, out string workspaceId)
    {
        workspaceId = string.Empty;
        if (requestState is null ||
            !requestState.StartsWith(_workspacePrefix, StringComparison.Ordinal) ||
            requestState.Length != _workspacePrefix.Length + 32 ||
            !Guid.TryParseExact(requestState.AsSpan(_workspacePrefix.Length), "N", out var parsed))
        {
            return false;
        }

        workspaceId = parsed.ToString("N");
        return true;
    }

    /// <summary>
    /// Attaches the workspace identity visible in <paramref name="arguments"/> to an MRTR signal
    /// before a temporary-dispatch scope restores the caller's original arguments. Existing
    /// producer-owned request state wins; this helper never overwrites it.
    /// </summary>
    internal static void PreserveWorkspaceId(
        InputRequiredException inputRequired,
        IDictionary<string, JsonElement>? arguments,
        string workspaceIdParameterName)
    {
        ArgumentNullException.ThrowIfNull(inputRequired);
        inputRequired.Result.RequestState ??=
            CaptureWorkspaceId(arguments, workspaceIdParameterName);
    }
}
