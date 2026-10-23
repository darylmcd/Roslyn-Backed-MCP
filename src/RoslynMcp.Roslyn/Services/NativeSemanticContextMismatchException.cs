using System.Collections.Immutable;

namespace RoslynMcp.Roslyn.Services;

internal enum NativeSemanticContextDifference { Context, Options, Output, Inputs, References }

/// <summary>A demonstrated semantic divergence, separate from IO, producer, or authority failures.</summary>
internal sealed class NativeSemanticContextMismatchException(NativeSemanticContextDifference difference,
    ImmutableArray<CompilerArgumentParseReceipt> parsing)
    : Exception("The independently loaded semantic snapshot differs from the native selected compiler context (" + difference + ").")
{
    internal ImmutableArray<CompilerArgumentParseReceipt> Parsing { get; } = parsing;
}
