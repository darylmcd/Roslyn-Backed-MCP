using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ConsumerLib.Generators;

[Generator]
public sealed class GreetingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static postInitializationContext =>
        {
            var source = """
                namespace ConsumerLib.Generated;

                public static class GeneratedGreeting
                {
                    public static string Message => "Hello from a generated document";
                }
                """;

            postInitializationContext.AddSource("GeneratedGreeting.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}
