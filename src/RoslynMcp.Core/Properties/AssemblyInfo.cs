using System.Runtime.CompilerServices;

// Keep parsed command identities private until gated result enrichment finishes.
[assembly: InternalsVisibleTo("RoslynMcp.Roslyn")]
[assembly: InternalsVisibleTo("RoslynMcp.Host.Stdio")]
[assembly: InternalsVisibleTo("RoslynMcp.Tests")]
