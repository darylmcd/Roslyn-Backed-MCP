using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace RoslynMcp.Tests;

/// <summary>
/// Assembly-wide ratchet for the <c>param-description-dedupe</c> sweep.
/// </summary>
/// <remarks>
/// <para>
/// Supersedes the per-slice <c>Type[]</c> guards (<see cref="ParameterDescriptionCanonicalizationTests"/>
/// and <see cref="ParamDescriptionCanonicalFormCodeActionSuppressionTests"/>), which could only see the
/// types their own slice swept and therefore could not stop a THIRD phrasing appearing in an unswept file.
/// This class walks every <c>[McpServerTool]</c> method in the Host.Stdio assembly instead.
/// </para>
/// <para>
/// PRs #1349 and #1350 shipped two mutually incompatible "canonical" forms in the same sweep.
/// <see cref="_canonicalWorkspaceId"/> (#1349's short form) is THE canon going forward — it is the only
/// one of the two that actually cuts characters. <see cref="_legacyWorkspaceId"/> (#1350's long form, and
/// the pre-existing repo-wide boilerplate) is tolerated only while unswept files remain, and only under a
/// monotonically-decreasing ceiling. A third phrasing is rejected outright.
/// </para>
/// </remarks>
[TestClass]
public sealed class ToolParameterCanonicalFormTests
{
    /// <summary>PR #1349's short form — the canonical target for every <c>workspaceId</c> parameter.</summary>
    private const string _canonicalWorkspaceId = "Workspace session id from workspace_load.";

    /// <summary>PR #1350's long form — the legacy boilerplate later slices must retire.</summary>
    private const string _legacyWorkspaceId = "The workspace session identifier returned by workspace_load";

    /// <summary>
    /// Reflection-observed count of <c>workspaceId</c> parameters still on <see cref="_legacyWorkspaceId"/>
    /// after the <c>param-description-dedupe-workspace-ops</c> slice. This is a RATCHET: each later slice
    /// lowers it and never raises it. The comparison is <c>&lt;=</c> so sibling slices landing in either
    /// order cannot red this test.
    /// </summary>
    private const int _legacyWorkspaceIdCeiling = 122;

    /// <summary>
    /// (tool name, parameter name) pairs whose description is deliberately neither form because it
    /// carries call-accuracy information both one-liners would lose. Mirrors the
    /// <c>set_diagnostic_severity</c>/<c>filePath</c> exception in
    /// <see cref="ParamDescriptionCanonicalFormCodeActionSuppressionTests"/>.
    /// </summary>
    /// <remarks>
    /// This list is a closed set on purpose. Adding to it is a deliberate act that says "this tool's
    /// <c>workspaceId</c> genuinely needs more than the one-liner" — it is NOT the escape hatch for
    /// drift. Everything not listed here must be one of the two known forms.
    /// </remarks>
    private static readonly HashSet<(string ToolName, string ParamName)> _loadBearingWorkspaceIdExceptions =
    [
        // Two-workspace tool: "source" distinguishes the workspace being forked FROM the fork.
        ("workspace_fork_apply", "workspaceId"),

        // Read-path tools whose workspaceId is OPTIONAL: the description documents the
        // omit-when-exactly-one-workspace auto-resolution contract enforced by the read-path
        // middleware. Dropping that to the one-liner would make the parameter look required.
        ("compile_check", "workspaceId"),
        ("go_to_definition", "workspaceId"),
        ("find_references", "workspaceId"),
        ("document_symbols", "workspaceId"),
        ("workspace_readiness_report", "workspaceId"),
        ("workspace_support_bundle", "workspaceId"),
    ];

    /// <summary>
    /// Tool types already swept onto <see cref="_canonicalWorkspaceId"/>: PR #1349's four
    /// (<c>param-description-dedupe-edit-file-ops</c>) plus this slice's four
    /// (<c>param-description-dedupe-workspace-ops</c>). These may carry ONLY the short form.
    /// </summary>
    private static readonly HashSet<string> _canonicalizedToolTypes =
    [
        nameof(EditTools),
        nameof(FileOperationTools),
        nameof(MSBuildTools),
        nameof(TypeMoveTools),
        nameof(WorkspaceWarmTools),
        nameof(WorkspaceDriftTool),
        nameof(UndoTools),
        nameof(EditorConfigTools),
    ];

    [TestMethod]
    public void EveryWorkspaceIdDescription_IsOneOfTheTwoKnownForms()
    {
        var failures = new List<string>();
        var observed = 0;

        foreach (var entry in EnumerateToolParameters())
        {
            if (entry.Parameter.Name != "workspaceId") continue;
            if (_loadBearingWorkspaceIdExceptions.Contains((entry.ToolName, "workspaceId"))) continue;

            observed++;
            if (string.Equals(entry.Description, _canonicalWorkspaceId, StringComparison.Ordinal)) continue;
            if (string.Equals(entry.Description, _legacyWorkspaceId, StringComparison.Ordinal)) continue;

            failures.Add(
                $"{entry.DeclaringTypeName}.{entry.MethodName} (tool {entry.ToolName}): got \"{entry.Description}\".");
        }

        Assert.IsTrue(observed > 0, "No 'workspaceId' tool parameters discovered — the assembly walk broke.");
        Assert.AreEqual(
            0,
            failures.Count,
            "A 'workspaceId' parameter description must be exactly one of:\n"
            + $"  canonical (target): \"{_canonicalWorkspaceId}\"\n"
            + $"  legacy (being retired): \"{_legacyWorkspaceId}\"\n"
            + "Do not invent a third phrasing — see backlog row param-description-dedupe. Offenders:\n  "
            + string.Join("\n  ", failures));
    }

    [TestMethod]
    public void LegacyWorkspaceIdSites_DoNotExceedTheRatchetCeiling()
    {
        var legacySites = EnumerateToolParameters()
            .Where(entry => entry.Parameter.Name == "workspaceId")
            .Where(entry => !_loadBearingWorkspaceIdExceptions.Contains((entry.ToolName, "workspaceId")))
            .Where(entry => string.Equals(entry.Description, _legacyWorkspaceId, StringComparison.Ordinal))
            .ToArray();

        Assert.IsTrue(
            legacySites.Length <= _legacyWorkspaceIdCeiling,
            $"Legacy-form 'workspaceId' descriptions rose to {legacySites.Length}, above the ratchet ceiling "
            + $"of {_legacyWorkspaceIdCeiling}. New tools must use \"{_canonicalWorkspaceId}\"; a slice that "
            + "retires legacy sites should LOWER _legacyWorkspaceIdCeiling to the new observed count.");
    }

    [TestMethod]
    public void CanonicalizedToolTypes_UseOnlyTheShortForm()
    {
        var failures = new List<string>();
        var observed = 0;

        foreach (var entry in EnumerateToolParameters())
        {
            if (entry.Parameter.Name != "workspaceId") continue;
            if (!_canonicalizedToolTypes.Contains(entry.DeclaringTypeName)) continue;

            observed++;
            if (string.Equals(entry.Description, _canonicalWorkspaceId, StringComparison.Ordinal)) continue;

            failures.Add(
                $"{entry.DeclaringTypeName}.{entry.MethodName} (tool {entry.ToolName}): got \"{entry.Description}\".");
        }

        Assert.IsTrue(
            observed > 0,
            "No 'workspaceId' parameters discovered on the already-canonicalized tool types — "
            + "_canonicalizedToolTypes is stale or the assembly walk broke.");
        Assert.AreEqual(
            0,
            failures.Count,
            $"Already-canonicalized tool types must carry exactly \"{_canonicalWorkspaceId}\":\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Walks every <c>[McpServerTool]</c>-attributed method in the Host.Stdio assembly, mirroring the
    /// discovery walk in <c>ToolParameterIndex.BuildIndex</c>, and yields each caller-supplied parameter
    /// that carries a <see cref="DescriptionAttribute"/>.
    /// </summary>
    private static IEnumerable<ToolParameterEntry> EnumerateToolParameters()
    {
        // Anchor on a known tool-host type so we walk the same assembly MCP discovery walks.
        var assembly = typeof(AnalysisTools).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var serverTool = method.GetCustomAttribute<McpServerToolAttribute>();
                if (serverTool is null) continue;

                // Inherited methods surface once per declaring type; keep the declaring type authoritative.
                if (method.DeclaringType != type) continue;

                var toolName = serverTool.Name ?? method.Name;

                foreach (var parameter in method.GetParameters())
                {
                    var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
                    if (description is null) continue;

                    yield return new ToolParameterEntry(
                        type.Name, method.Name, toolName, parameter, description);
                }
            }
        }
    }

    private readonly record struct ToolParameterEntry(
        string DeclaringTypeName,
        string MethodName,
        string ToolName,
        ParameterInfo Parameter,
        string Description);
}
