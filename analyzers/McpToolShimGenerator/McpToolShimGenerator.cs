// Copyright (c) darylmcd. Licensed under the MIT License.
//
// McpToolShimGenerator — Roslyn source generator that will (in phase 1.2+) emit the
// hand-written 7-line dispatch shims under src/RoslynMcp.Host.Stdio/Tools/*Tools.cs
// directly from service-interface method annotations ([McpServerTool] +
// [McpToolMetadata] + [GeneratedDispatch]).
//
// Phase 1.1 (this file): the generator is wired into the pipeline but produces no
// output. It exists so that phase 1.2+ can extend its emitter without touching the
// MSBuild wiring (csproj ProjectReference + .slnx entry). No [GeneratedDispatch]-
// annotated methods exist yet, so the ForAttributeWithMetadataName pipeline yields
// zero elements and RegisterSourceOutput is never invoked.
//
// See ai_docs/plans/20260421T123658Z_post-audit-followups.md § "Workstream 1 —
// phase 1.1" for the full rollout plan across 7 PRs.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Analyzers.McpToolShim;

/// <summary>
/// Incremental source generator that emits MCP tool dispatch shim methods for every
/// service-interface method annotated with <c>[GeneratedDispatch]</c>. The generator
/// complements the hand-written <c>RoslynMcp.Host.Stdio.Tools.ToolDispatch</c> runtime
/// helper — the helper owns the shared lock / serialization path; the generator
/// produces the per-tool entry points that delegate into it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.1 status:</b> no-op scaffold. The pipeline is set up but no service
/// interface carries <c>[GeneratedDispatch]</c> yet, so no output is emitted. Phase
/// 1.2 will migrate the first tool group (<c>CodeActionTools</c>) and replace the
/// no-op <see cref="IncrementalGeneratorInitializationContext.RegisterSourceOutput{T}"/>
/// callback with a real per-tool emitter (<c>CodeActionTools.g.cs</c>,
/// <c>BulkRefactoringTools.g.cs</c>, …).
/// </para>
///
/// <para>
/// <b>Why a generator, not a hand-extracted helper?</b> A <c>ToolDispatch.ApplyAsync</c>
/// helper would save ~200 LOC of duplicated shim bodies but still require every new
/// tool to be hand-added to <c>Tools/*.cs</c> AND <c>ServerSurfaceCatalog.cs</c>. The
/// generator eliminates BOTH hand-maintenance points by treating the service
/// interfaces as the single source of truth. See the plan for the counter-arguments
/// considered.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class McpToolShimGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Fully-qualified metadata name of the marker attribute. The generator uses
    /// <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/> which matches
    /// by this exact FQN — a typo in the attribute namespace silently yields zero
    /// matches rather than failing the build, so the name is centralized here.
    /// </summary>
    public const string GeneratedDispatchAttributeMetadataName = "RoslynMcp.Core.Attributes.GeneratedDispatchAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Phase 1.1 no-op pipeline. Kept in the shape the phase-1.2 emitter will
        // extend:
        //   - `annotatedMethods` selects every method syntax node carrying the
        //     [GeneratedDispatch] attribute. Today: 0 matches repo-wide.
        //   - `RegisterSourceOutput` will, in phase 1.2+, accumulate per-tool-group
        //     emissions and call `spc.AddSource("CodeActionTools.g.cs", …)`.
        //
        // Why keep the pipeline instead of `return;`? Because MSBuild's analyzer
        // pipeline treats a generator with zero registered outputs as "no work" —
        // including not failing the build when the generator assembly fails to
        // load. Registering a real (even if empty) output makes a missing /
        // corrupt analyzer assembly fail loudly during the host project build.
        var annotatedMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            GeneratedDispatchAttributeMetadataName,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (ctx, _) => ctx.TargetNode);

        context.RegisterSourceOutput(annotatedMethods, static (_, _) =>
        {
            // Intentional no-op for phase 1.1. Phase 1.2 replaces this with the real
            // per-tool shim emitter (one .g.cs per containing service interface's
            // Tools/*.cs counterpart). Today zero annotated methods exist, so this
            // callback never fires; leaving the registration in place lets the
            // pipeline shape stay intact across the phase-1.2 migration.
        });
    }
}
