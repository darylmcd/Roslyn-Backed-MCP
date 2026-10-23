using System.Reflection;
using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Complete observed host settings from the supported native Framework value type.</summary>
internal sealed record NativeTaskHostParameters(string? Runtime, string? Architecture, string? DotnetHostPath,
    string? MSBuildAssemblyPath, bool? TaskHostFactoryExplicitlyRequested)
{
    public override string ToString() => "NativeTaskHostParameters";
    internal bool UsesDefaultHost => DotnetHostPath is null && MSBuildAssemblyPath is null && TaskHostFactoryExplicitlyRequested is not true;

    internal static NativeTaskHostParameters Read(object value, Type declaredType)
    {
        var type = value.GetType();
        if (!type.IsValueType || type.FullName != "Microsoft.Build.Framework.TaskHostParameters"
            || type.Assembly != typeof(TaskPropertyInfo).Assembly || declaredType != type
            || type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Length != 5) throw new NativeTaskRegistryUnavailableException();
        var requested = Property("TaskHostFactoryExplicitlyRequested", typeof(bool?)).GetValue(value);
        return new(String("Runtime"), String("Architecture"), String("DotnetHostPath"), String("MSBuildAssemblyPath"),
            requested is null ? null : requested is bool flag ? flag : throw new NativeTaskRegistryUnavailableException());

        PropertyInfo Property(string name, Type expected)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public) ?? throw new NativeTaskRegistryUnavailableException();
            if (property.PropertyType != expected || property.DeclaringType != type || property.GetIndexParameters().Length != 0)
                throw new NativeTaskRegistryUnavailableException();
            return property;
        }
        string? String(string name) => Property(name, typeof(string)).GetValue(value) is { } item
            ? item as string ?? throw new NativeTaskRegistryUnavailableException() : null;
    }
}
