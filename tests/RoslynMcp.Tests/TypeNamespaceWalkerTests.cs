using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class TypeNamespaceWalkerTests
{
    [TestMethod]
    public async Task Walks_Generic_Arguments_Recursively()
    {
        var compilation = await CompileAsync(@"
            using System.Collections.Generic;
            using System.Threading.Tasks;
            namespace Outer { public class Foo { } }
            namespace Caller {
                public class Target {
                    public Task<List<Outer.Foo>> Method() => null!;
                }
            }
        ");

        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Caller.Target")!
            .GetMembers("Method").Single();

        var namespaces = new HashSet<string>();
        TypeNamespaceWalker.Collect(namespaces, method.ReturnType, ownNamespace: "Caller");

        CollectionAssert.Contains(namespaces.ToArray(), "System.Threading.Tasks");
        CollectionAssert.Contains(namespaces.ToArray(), "System.Collections.Generic");
        CollectionAssert.Contains(namespaces.ToArray(), "Outer");
    }

    [TestMethod]
    public async Task Skips_Type_Parameter_Symbols()
    {
        var compilation = await CompileAsync(@"
            namespace N { public class Box<T> { public T Value => default!; } }
        ");

        var box = compilation.GetTypeByMetadataName("N.Box`1")!;
        var typeParam = box.TypeParameters[0];

        var namespaces = new HashSet<string>();
        TypeNamespaceWalker.Collect(namespaces, typeParam, ownNamespace: null);

        Assert.AreEqual(0, namespaces.Count, "Type-parameter symbols have no containing namespace.");
    }

    [TestMethod]
    public async Task Skips_Own_Namespace()
    {
        var compilation = await CompileAsync(@"
            namespace Same { public class A { } public class B { public A Field => null!; } }
        ");

        var b = compilation.GetTypeByMetadataName("Same.B")!;
        var field = (IPropertySymbol)b.GetMembers("Field").Single();

        var namespaces = new HashSet<string>();
        TypeNamespaceWalker.Collect(namespaces, field.Type, ownNamespace: "Same");

        Assert.AreEqual(0, namespaces.Count, "Symbols in the consumer's own namespace must be skipped.");
    }

    [TestMethod]
    public async Task Walks_Array_Element_Types()
    {
        var compilation = await CompileAsync(@"
            namespace Outer { public class Item { } }
            namespace Caller {
                public class Target { public Outer.Item[] Field => null!; }
            }
        ");

        var target = compilation.GetTypeByMetadataName("Caller.Target")!;
        var field = (IPropertySymbol)target.GetMembers("Field").Single();

        var namespaces = new HashSet<string>();
        TypeNamespaceWalker.Collect(namespaces, field.Type, ownNamespace: "Caller");

        CollectionAssert.Contains(namespaces.ToArray(), "Outer");
    }

    private static Task<Compilation> CompileAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            });
        return Task.FromResult<Compilation>(compilation);
    }
}
