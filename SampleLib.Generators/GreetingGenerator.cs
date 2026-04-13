using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SampleLib.Generators;

[Generator]
public sealed class GreetingGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var source = """
            namespace SampleLib.Generated;

            public static class GeneratedGreeting
            {
                public static string Message => "Hello from a generated document";
            }
            """;

        context.AddSource("GeneratedGreeting.g.cs", SourceText.From(source, Encoding.UTF8));
    }
}
