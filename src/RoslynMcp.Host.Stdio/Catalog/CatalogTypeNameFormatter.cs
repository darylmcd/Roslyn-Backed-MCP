namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Returns a stable, JSON-friendly type label for catalog parameter-schema rows. Nullable value
/// types collapse to <c>{type}?</c>; reference types (which are nullable in the C# 8+ annotation
/// model but which reflection cannot fully introspect for nullable-annotation context) keep the
/// bare name. Generic types format as <c>List&lt;string&gt;</c> rather than CLR backtick-arity.
/// Common primitives map to their C# keyword form. Shared by <see cref="ToolParameterIndex"/> and
/// <see cref="PromptParameterIndex"/> (previously duplicated verbatim in both).
/// </summary>
internal static class CatalogTypeNameFormatter
{
    public static string FormatTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return FormatTypeName(underlying) + "?";

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tickIdx = name.IndexOf('`', StringComparison.Ordinal);
            if (tickIdx >= 0) name = name[..tickIdx];
            var args = type.GetGenericArguments().Select(FormatTypeName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        // Map common primitives to their C# keyword form for readability.
        return type.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Boolean" => "bool",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Decimal" => "decimal",
            "System.Byte" => "byte",
            "System.Object" => "object",
            _ => type.Name,
        };
    }
}
