using System.Reflection;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Owns the prompt-parameter boundary shared by catalog publication, prompt shim binding, and
/// pre-dispatch wire validation. Service and cancellation parameters are supplied by the host;
/// every remaining parameter is caller-owned input.
/// </summary>
internal static class PromptParameterClassifier
{
    internal static bool IsCallerInput(ParameterInfo parameter) =>
        parameter.ParameterType != typeof(CancellationToken) &&
        !IsServiceType(parameter.ParameterType);

    internal static bool IsServiceType(Type type) =>
        type.IsInterface ||
        type.Namespace?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true;
}
