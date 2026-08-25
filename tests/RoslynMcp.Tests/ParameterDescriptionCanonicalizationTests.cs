using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the parameter-description canonicalization sweep.
/// </summary>
/// <remarks>
/// Scope is deliberately limited to the four tool types swept by
/// <c>param-description-dedupe-edit-file-ops</c>. The remaining <c>Tools/*.cs</c> files are still
/// unswept, so a repo-wide assertion would red CI; later slices widen <see cref="SweptToolTypes"/>.
/// </remarks>
[TestClass]
public sealed class ParameterDescriptionCanonicalizationTests
{
    private static readonly Type[] SweptToolTypes =
    [
        typeof(EditTools),
        typeof(FileOperationTools),
        typeof(MSBuildTools),
        typeof(TypeMoveTools),
    ];

    private const string CanonicalWorkspaceId = "Workspace session id from workspace_load.";

    private static readonly Regex CanonicalPreviewToken =
        new(@"^Preview token from [a-z0-9_]+_preview\.$", RegexOptions.Compiled);

    private static readonly Regex CanonicalPath =
        new(@"^Absolute path to the .+\.$", RegexOptions.Compiled);

    /// <summary>(type name, parameter name) -> substring the description must still contain.</summary>
    private static readonly (string Type, string Parameter, string Keyword)[] LoadBearingContracts =
    [
        (nameof(EditTools), "edits", "1-based"),
        (nameof(EditTools), "verify", "compile_check"),
        (nameof(EditTools), "autoRevertOnError", "Ignored when verify is false"),
        (nameof(MSBuildTools), "includedNames", "native JSON array"),
    ];

    private static IEnumerable<(Type Type, MethodInfo Method, ParameterInfo Parameter, string Description)> SweptToolParameters()
    {
        foreach (var type in SweptToolTypes)
        {
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                {
                    continue;
                }

                foreach (var parameter in method.GetParameters())
                {
                    var description = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
                    if (description is not null)
                    {
                        yield return (type, method, parameter, description);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void SweptTools_BoilerplateParameterDescriptions_UseCanonicalForm()
    {
        var violations = new List<string>();

        foreach (var (type, method, parameter, description) in SweptToolParameters())
        {
            var site = $"{type.Name}.{method.Name}({parameter.Name})";

            switch (parameter.Name)
            {
                case "workspaceId" when !string.Equals(description, CanonicalWorkspaceId, StringComparison.Ordinal):
                    violations.Add($"{site}: expected \"{CanonicalWorkspaceId}\", got \"{description}\"");
                    break;
                case "previewToken" when !CanonicalPreviewToken.IsMatch(description):
                    violations.Add($"{site}: expected \"Preview token from <tool>_preview.\", got \"{description}\"");
                    break;
                // Optional path parameters (e.g. TypeMoveTools.targetFilePath) keep their
                // "Optional: ... defaults to ..." form — the defaulting rule is a discriminator,
                // not boilerplate, so only required path parameters carry the canonical one-liner.
                case "filePath" or "sourceFilePath" or "targetFilePath"
                    when !parameter.IsOptional && !CanonicalPath.IsMatch(description):
                    violations.Add($"{site}: expected \"Absolute path to the <role>.\", got \"{description}\"");
                    break;
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Boilerplate parameter descriptions on the swept tool types must use the canonical form:\n  "
            + string.Join("\n  ", violations));
    }

    [TestMethod]
    public void SweptTools_LoadBearingParameterDescriptions_RetainTheirContractKeyword()
    {
        var parameters = SweptToolParameters().ToArray();
        var violations = new List<string>();

        foreach (var (typeName, parameterName, keyword) in LoadBearingContracts)
        {
            var matches = parameters
                .Where(entry => entry.Type.Name == typeName && entry.Parameter.Name == parameterName)
                .ToArray();

            if (matches.Length == 0)
            {
                violations.Add($"{typeName}.{parameterName}: no [Description]-carrying tool parameter found");
                continue;
            }

            foreach (var match in matches)
            {
                if (!match.Description.Contains(keyword, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{typeName}.{match.Method.Name}({parameterName}): must still document \"{keyword}\"");
                }
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Load-bearing parameter descriptions must not be trimmed past their contract:\n  "
            + string.Join("\n  ", violations));
    }
}
