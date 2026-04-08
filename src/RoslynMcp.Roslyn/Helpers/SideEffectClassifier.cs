using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Recognizes side-effect API calls (disk IO, network, process spawn, database) inside a
/// method body. Backs the side-effect detection branch of <c>find_type_mutations</c>:
/// previously the tool only flagged instance-field reassignments, so a class like
/// <c>FileSnapshotStore</c> whose entire purpose is disk IO reported zero mutations.
/// The catalog uses fully-qualified <c>(namespace, type, method-prefix)</c> tuples; matches
/// are checked against the symbol resolved by the semantic model so namespace aliases and
/// using directives don't fool the classifier.
/// </summary>
public static class SideEffectClassifier
{
    /// <summary>
    /// Names matching the broad scope of side-effects detected by this classifier.
    /// </summary>
    public static class Scopes
    {
        public const string FieldWrite = "FieldWrite";
        public const string CollectionWrite = "CollectionWrite";
        public const string IO = "IO";
        public const string Network = "Network";
        public const string Process = "Process";
        public const string Database = "Database";
    }

    /// <summary>
    /// Walks the method body and returns the highest-severity side-effect scope found, or
    /// <see langword="null"/> if the method does not call any catalogued side-effect API.
    /// Order: Database &gt; Network &gt; Process &gt; IO. (Database is highest because it
    /// crosses the most boundaries; IO is lowest because every disk read counts.)
    /// </summary>
    public static string? ClassifyMethodSideEffects(SyntaxNode methodNode, SemanticModel semanticModel, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(methodNode);
        ArgumentNullException.ThrowIfNull(semanticModel);

        string? best = null;
        foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is not IMethodSymbol method)
            {
                continue;
            }

            var scope = ClassifyMethod(method);
            best = HigherSeverity(best, scope);
        }

        // Also check object creations like `new FileStream(...)` or `new HttpClient(...)`.
        foreach (var creation in methodNode.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var typeInfo = semanticModel.GetTypeInfo(creation, ct);
            if (typeInfo.Type is not INamedTypeSymbol type)
            {
                continue;
            }

            var scope = ClassifyTypeConstruction(type);
            best = HigherSeverity(best, scope);
        }

        return best;
    }

    /// <summary>
    /// Classifies a single method symbol against the catalogued side-effect APIs.
    /// </summary>
    private static string? ClassifyMethod(IMethodSymbol method)
    {
        var ns = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var typeName = method.ContainingType?.Name ?? string.Empty;
        var methodName = method.Name;

        return (ns, typeName) switch
        {
            ("System.IO", "File") when methodName.StartsWith("Write", StringComparison.Ordinal)
                || methodName.StartsWith("Append", StringComparison.Ordinal)
                || methodName is "Delete" or "Move" or "Copy" or "Replace" or "Create" => Scopes.IO,
            ("System.IO", "Directory") when methodName.StartsWith("Create", StringComparison.Ordinal)
                || methodName is "Delete" or "Move" => Scopes.IO,
            ("System.IO", "StreamWriter") when methodName.StartsWith("Write", StringComparison.Ordinal)
                || methodName is "Flush" => Scopes.IO,
            ("System.IO", "FileStream") when methodName.StartsWith("Write", StringComparison.Ordinal)
                || methodName is "Flush" => Scopes.IO,
            ("System.Net.Http", "HttpClient") when methodName.StartsWith("Send", StringComparison.Ordinal)
                || methodName.StartsWith("Get", StringComparison.Ordinal)
                || methodName.StartsWith("Post", StringComparison.Ordinal)
                || methodName.StartsWith("Put", StringComparison.Ordinal)
                || methodName.StartsWith("Delete", StringComparison.Ordinal)
                || methodName.StartsWith("Patch", StringComparison.Ordinal) => Scopes.Network,
            ("System.Net.Sockets", "TcpClient" or "UdpClient") when methodName is "Connect" or "Send" or "ConnectAsync" or "SendAsync" => Scopes.Network,
            ("System.Diagnostics", "Process") when methodName is "Start" or "Kill" => Scopes.Process,
            ("System.Data.Common", "DbCommand") when methodName.StartsWith("Execute", StringComparison.Ordinal) => Scopes.Database,
            _ => null,
        };
    }

    /// <summary>
    /// Classifies a single type-construction expression. Treats constructing a side-effect
    /// type (e.g. <c>new FileStream(path, FileMode.Create)</c>) as the side-effect itself.
    /// </summary>
    private static string? ClassifyTypeConstruction(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return (ns, type.Name) switch
        {
            ("System.IO", "FileStream" or "StreamWriter") => Scopes.IO,
            ("System.Net.Http", "HttpClient") => Scopes.Network,
            ("System.Net.Sockets", "TcpClient" or "UdpClient") => Scopes.Network,
            ("System.Diagnostics", "Process" or "ProcessStartInfo") => Scopes.Process,
            _ => null,
        };
    }

    /// <summary>
    /// Severity ordering: Database &gt; Network &gt; Process &gt; IO &gt; CollectionWrite &gt; FieldWrite.
    /// Returns the higher of the two scopes (or the non-null one).
    /// </summary>
    private static string? HigherSeverity(string? a, string? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return Rank(a) >= Rank(b) ? a : b;
    }

    private static int Rank(string scope) => scope switch
    {
        Scopes.Database => 5,
        Scopes.Network => 4,
        Scopes.Process => 3,
        Scopes.IO => 2,
        Scopes.CollectionWrite => 1,
        Scopes.FieldWrite => 0,
        _ => -1,
    };
}
