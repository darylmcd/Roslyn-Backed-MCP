using System.Reflection;
using System.Reflection.Emit;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Exercises interface dispatch through an otherwise complete legacy implementation.</summary>
internal static class RequiredInterfaceContractProbe
{
    internal static async Task RejectsLegacyOnlyAsync(Type contract, string name, int parameterCount,
        Func<ParameterInfo, object?> argument)
    {
        var streaming = contract.GetMethods().Single(method => method.Name == name && method.GetParameters().Length == parameterCount);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LegacyStreamingProbe" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Probe").DefineType("LegacyOnly", TypeAttributes.Public | TypeAttributes.Sealed);
        builder.AddInterfaceImplementation(contract);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var calls = builder.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        foreach (var method in contract.GetInterfaces().SelectMany(type => type.GetMethods()).Concat(contract.GetMethods()).Distinct())
        {
            if (method == streaming || !method.IsAbstract) continue;
            var implementation = builder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                method.ReturnType, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            var il = implementation.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, calls);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stsfld, calls);
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, typeof(Task).GetMethods().Single(candidate => candidate.Name == nameof(Task.FromResult)).MakeGenericMethod(method.ReturnType.GenericTypeArguments));
            }
            else if (method.ReturnType != typeof(void)) il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            builder.DefineMethodOverride(implementation, method);
        }
        Type legacy;
        try { legacy = builder.CreateType()!; }
        catch (TypeLoadException)
        {
            Assert.IsTrue(streaming.IsAbstract, "Only the selected required member may reject this otherwise complete implementation.");
            return;
        }
        var instance = Activator.CreateInstance(legacy);
        var arguments = streaming.GetParameters().Select(argument).ToArray();
        await (Task)streaming.Invoke(instance, arguments)!;
        var count = (int)legacy.GetField("Calls")!.GetValue(null)!;
        Assert.Fail($"A state-bearing interface request reached a legacy implementation ({count} legacy calls) without accepting its required prepared state.");
    }
}
