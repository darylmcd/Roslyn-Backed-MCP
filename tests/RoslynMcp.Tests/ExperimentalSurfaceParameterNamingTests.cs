using System.Reflection;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// parameter-naming-canonicalization-migration: lock the canonical wire-parameter name for
/// the project-scope filter across the experimental tool surface to <c>projectName</c>.
///
/// Five experimental tools historically exposed non-canonical wire keys —
/// <c>find_dead_locals</c>, <c>find_dead_fields</c>, <c>find_duplicate_helpers</c>, and
/// <c>semantic_grep</c> used <c>projectFilter</c>; <c>trace_exception_flow</c> used
/// <c>scopeProjectFilter</c>. Every other tool that scopes by project already uses
/// <c>projectName</c>, so these were the outliers a consumer had to special-case.
///
/// The reflection sweep below is the regression guard: it walks every <c>[McpServerTool]</c>
/// method annotated <c>experimental</c> via <see cref="McpToolMetadataAttribute"/> and asserts
/// none of them exposes a <c>projectFilter</c> or <c>scopeProjectFilter</c> wire parameter. A
/// missed rename — or a future experimental tool that reintroduces the old name — fails here.
///
/// NOTE — negative space: STABLE-tier tools that still legitimately expose <c>projectFilter</c>
/// (e.g. <c>find_duplicated_methods</c> / <c>find_duplicated_code</c> in AdvancedAnalysisTools,
/// <c>find_type_consumers</c> in ConsumerAnalysisTools) are intentionally out of scope of this
/// migration and are NOT asserted on — the SupportTier filter excludes them.
/// </summary>
[TestClass]
public sealed class ExperimentalSurfaceParameterNamingTests
{
    // The wire parameter names this migration eliminated from the experimental surface.
    private static readonly string[] BannedExperimentalParameterNames = ["projectFilter", "scopeProjectFilter"];

    // The five experimental tools whose project-scope wire parameter was renamed to projectName.
    // Pinned by name so the positive assertion below survives a tool being renamed or retired
    // (a stale entry surfaces as a discovery failure rather than silently passing).
    private static readonly string[] RenamedExperimentalTools =
    [
        "find_dead_locals",
        "find_dead_fields",
        "find_duplicate_helpers",
        "semantic_grep",
        "trace_exception_flow",
    ];

    [TestMethod]
    public void NoExperimentalTool_ExposesNonCanonicalProjectScopeParameter()
    {
        var failures = new List<string>();
        foreach (var (toolName, method) in EnumerateExperimentalToolMethods())
        {
            foreach (var param in method.GetParameters())
            {
                if (BannedExperimentalParameterNames.Contains(param.Name, StringComparer.Ordinal))
                {
                    failures.Add(
                        $"{toolName} ({method.DeclaringType!.Name}.{method.Name}): exposes wire parameter " +
                        $"'{param.Name}'. Experimental tools must use the canonical 'projectName' for the " +
                        "project-scope filter (parameter-naming-canonicalization-migration).");
                }
            }
        }

        Assert.AreEqual(0, failures.Count,
            "Experimental tools expose non-canonical project-scope parameter names:\n\n" +
            string.Join("\n", failures));
    }

    [TestMethod]
    public void RenamedExperimentalTools_ExposeProjectNameWireParameter()
    {
        var methodsByToolName = EnumerateExperimentalToolMethods()
            .ToDictionary(pair => pair.ToolName, pair => pair.Method, StringComparer.Ordinal);

        foreach (var toolName in RenamedExperimentalTools)
        {
            Assert.IsTrue(methodsByToolName.TryGetValue(toolName, out var method),
                $"Experimental tool '{toolName}' was not discovered — it was renamed, retired, or its " +
                "experimental tier flipped. Update RenamedExperimentalTools accordingly.");

            var hasProjectName = method!.GetParameters()
                .Any(p => string.Equals(p.Name, "projectName", StringComparison.Ordinal));

            Assert.IsTrue(hasProjectName,
                $"Tool '{toolName}' ({method.DeclaringType!.Name}.{method.Name}) must expose a 'projectName' " +
                "wire parameter after the canonicalization migration.");
        }
    }

    private static IEnumerable<(string ToolName, MethodInfo Method)> EnumerateExperimentalToolMethods()
    {
        var toolAssembly = typeof(ServerTools).Assembly;
        var experimentalMethods = toolAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Where(m => string.Equals(
                m.GetCustomAttribute<McpToolMetadataAttribute>()?.SupportTier,
                "experimental",
                StringComparison.Ordinal))
            .Select(m => (ToolName: m.GetCustomAttribute<McpServerToolAttribute>()!.Name!, Method: m))
            .ToList();

        Assert.IsTrue(experimentalMethods.Count > 0,
            "No experimental [McpServerTool] methods were discovered — test fixture broken.");

        return experimentalMethods;
    }
}
