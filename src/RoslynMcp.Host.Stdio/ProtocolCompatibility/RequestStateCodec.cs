using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.ProtocolCompatibility;

/// <summary>
/// Encodes producer-owned state that must survive a modern MRTR hand-off and restores it on the
/// client's retry. MCP request state is client-visible and echoed by the client, so the compound
/// shape carries only opaque workspace/correlation identifiers and stable non-secret codes; it is
/// not an authorization or secrecy boundary.
/// </summary>
internal static class RequestStateCodec
{
    private const string _workspacePrefix = "roslynmcp.workspace.v1:";
    private const string _requestPrefix = "roslynmcp.request.v1:";
    private const string _emptyComponent = "-";
    private const string _siblingNameDiscoveryWarningCode = "sibling-name-discovery-incomplete";
    private const string _siblingNameDiscoveryWarningText =
        "Sibling test-name discovery was incomplete; sampled naming used the readable siblings. ";
    private const string _correlationIdPrefix = "correlationId=";
    private const int _maxRequestStateLength = 192;

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
        if (TryParseLegacyWorkspaceId(requestState, out workspaceId))
        {
            return true;
        }

        if (!TryParseCompoundState(requestState, out var state) || state.WorkspaceId is null)
        {
            return false;
        }

        workspaceId = state.WorkspaceId;
        return true;
    }

    internal static bool TryRestoreSiblingNameDiscoveryWarning(
        string? requestState,
        out string warning)
    {
        warning = string.Empty;
        if (!TryParseCompoundState(requestState, out var state) ||
            !string.Equals(
                state.WarningCode,
                _siblingNameDiscoveryWarningCode,
                StringComparison.Ordinal) ||
            state.CorrelationId is null)
        {
            return false;
        }

        warning = _siblingNameDiscoveryWarningText +
            PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(state.CorrelationId);
        return true;
    }

    /// <summary>
    /// Attaches the workspace identity visible in <paramref name="arguments"/> to an MRTR signal
    /// before a temporary-dispatch scope restores the caller's original arguments. Recognized
    /// producer-owned compound state is augmented; unknown state is never overwritten.
    /// </summary>
    internal static void PreserveWorkspaceId(
        InputRequiredException inputRequired,
        IDictionary<string, JsonElement>? arguments,
        string workspaceIdParameterName)
    {
        ArgumentNullException.ThrowIfNull(inputRequired);
        var capturedState = CaptureWorkspaceId(arguments, workspaceIdParameterName);
        if (!TryParseLegacyWorkspaceId(capturedState, out var workspaceId))
        {
            return;
        }

        var currentState = inputRequired.Result.RequestState;
        if (currentState is null)
        {
            inputRequired.Result.RequestState = capturedState;
            return;
        }

        if (TryParseCompoundState(currentState, out var state) && state.WorkspaceId is null)
        {
            inputRequired.Result.RequestState = EncodeCompoundState(state with
            {
                WorkspaceId = workspaceId,
            });
        }
    }

    /// <summary>
    /// Carries a sibling-discovery warning across MRTR without copying client prose into request
    /// state. Only the recognized warning code and its normalized public correlation identifier
    /// are retained; unknown or malformed producer state remains untouched.
    /// </summary>
    internal static void PreserveSiblingNameDiscoveryWarning(
        InputRequiredException inputRequired,
        string? warning)
    {
        ArgumentNullException.ThrowIfNull(inputRequired);
        if (!TryExtractSiblingNameDiscoveryCorrelationId(warning, out var correlationId))
        {
            return;
        }

        var warningState = new RequestStateParts(
            WorkspaceId: null,
            WarningCode: _siblingNameDiscoveryWarningCode,
            CorrelationId: correlationId);
        var currentState = inputRequired.Result.RequestState;
        if (currentState is null)
        {
            inputRequired.Result.RequestState = EncodeCompoundState(warningState);
            return;
        }

        if (TryParseLegacyWorkspaceId(currentState, out var workspaceId))
        {
            inputRequired.Result.RequestState = EncodeCompoundState(warningState with
            {
                WorkspaceId = workspaceId,
            });
            return;
        }

        if (TryParseCompoundState(currentState, out var state) && state.WarningCode is null)
        {
            inputRequired.Result.RequestState = EncodeCompoundState(state with
            {
                WarningCode = _siblingNameDiscoveryWarningCode,
                CorrelationId = correlationId,
            });
        }
    }

    private static bool TryParseLegacyWorkspaceId(string? requestState, out string workspaceId)
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

    private static bool TryExtractSiblingNameDiscoveryCorrelationId(
        string? warning,
        out string correlationId)
    {
        correlationId = string.Empty;
        if (warning is null ||
            !warning.StartsWith(_siblingNameDiscoveryWarningText, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = warning.AsSpan(_siblingNameDiscoveryWarningText.Length);
        if (!suffix.StartsWith(_correlationIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        correlationId = suffix[_correlationIdPrefix.Length..].ToString();
        return string.Equals(
            warning,
            _siblingNameDiscoveryWarningText +
                PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(correlationId),
            StringComparison.Ordinal);
    }

    private static bool TryParseCompoundState(
        string? requestState,
        out RequestStateParts state)
    {
        state = new RequestStateParts(null, null, null);
        if (requestState is null ||
            requestState.Length > _maxRequestStateLength ||
            !requestState.StartsWith(_requestPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var components = requestState[_requestPrefix.Length..].Split(':');
        if (components.Length != 3)
        {
            return false;
        }

        string? workspaceId = null;
        if (!string.Equals(components[0], _emptyComponent, StringComparison.Ordinal))
        {
            if (!Guid.TryParseExact(components[0], "N", out var parsedWorkspaceId))
            {
                return false;
            }

            workspaceId = parsedWorkspaceId.ToString("N");
        }

        string? warningCode = null;
        string? correlationId = null;
        var hasWarningCode = !string.Equals(components[1], _emptyComponent, StringComparison.Ordinal);
        var hasCorrelationId = !string.Equals(components[2], _emptyComponent, StringComparison.Ordinal);
        if (hasWarningCode != hasCorrelationId)
        {
            return false;
        }

        if (hasWarningCode)
        {
            if (!string.Equals(
                    components[1],
                    _siblingNameDiscoveryWarningCode,
                    StringComparison.Ordinal) ||
                !IsNormalizedCorrelationId(components[2]))
            {
                return false;
            }

            warningCode = components[1];
            correlationId = components[2];
        }

        if (workspaceId is null && warningCode is null)
        {
            return false;
        }

        state = new RequestStateParts(workspaceId, warningCode, correlationId);
        return true;
    }

    private static bool IsNormalizedCorrelationId(string correlationId) =>
        string.Equals(
            PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(correlationId),
            _correlationIdPrefix + correlationId,
            StringComparison.Ordinal);

    private static string EncodeCompoundState(RequestStateParts state) =>
        _requestPrefix +
        (state.WorkspaceId ?? _emptyComponent) + ":" +
        (state.WarningCode ?? _emptyComponent) + ":" +
        (state.CorrelationId ?? _emptyComponent);

    private sealed record RequestStateParts(
        string? WorkspaceId,
        string? WarningCode,
        string? CorrelationId);
}
