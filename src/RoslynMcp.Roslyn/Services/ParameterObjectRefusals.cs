using System.Runtime.CompilerServices;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

internal static class ParameterObjectRefusals
{
    // Keep the released exception types and data untouched. Weak, private ownership lets
    // read-only feasibility distinguish authored refusals from unexpected internal errors.
    private static readonly ConditionalWeakTable<Exception, Ownership> _owned = new();

    internal static PublicArgumentException Argument(string message, string paramName) =>
        Own(new PublicArgumentException(message, paramName));

    internal static InvalidOperationException InvalidOperation(string message) =>
        Own(InvalidOperationErrors.Internal(message));

    internal static bool IsExpected(Exception exception) =>
        exception is not OperationCanceledException && _owned.TryGetValue(exception, out _);

    private static T Own<T>(T exception) where T : Exception
    {
        _owned.Add(exception, new Ownership());
        return exception;
    }

    private sealed class Ownership
    {
    }
}
