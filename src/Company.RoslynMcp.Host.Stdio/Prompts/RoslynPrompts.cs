using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Prompts;

[McpServerPromptType]
public static class RoslynPrompts
{
    [McpServerPrompt(Name = "explain_error")]
    [Description("Generate a prompt to explain a compiler diagnostic error and suggest fixes")]
    public static async Task<IEnumerable<PromptMessage>> ExplainError(
        IDiagnosticService diagnosticService,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var details = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, ct).ConfigureAwait(false);
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);

        // Extract surrounding lines for context
        var contextLines = "";
        if (sourceText is not null)
        {
            var lines = sourceText.Split('\n');
            var startLine = Math.Max(0, line - 6);
            var endLine = Math.Min(lines.Length - 1, line + 4);
            contextLines = string.Join('\n', lines[startLine..endLine].Select((l, i) =>
            {
                var lineNum = startLine + i + 1;
                var marker = lineNum == line ? " >>> " : "     ";
                return $"{marker}{lineNum,4}: {l.TrimEnd('\r')}";
            }));
        }

        var detailsJson = JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Explain the following C# compiler diagnostic and suggest how to fix it.

                        **Diagnostic:** {diagnosticId}
                        **File:** {filePath}
                        **Line:** {line}, **Column:** {column}

                        **Diagnostic Details:**
                        ```json
                        {detailsJson}
                        ```

                        **Source Context:**
                        ```csharp
                        {contextLines}
                        ```

                        Please:
                        1. Explain what this diagnostic means in plain language
                        2. Explain why it occurs in this context
                        3. Suggest one or more fixes with code examples
                        4. Note any potential side effects of each fix
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "suggest_refactoring")]
    [Description("Generate a prompt to analyze code and suggest refactorings")]
    public static async Task<IEnumerable<PromptMessage>> SuggestRefactoring(
        IWorkspaceManager workspace,
        ISymbolService symbolService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("Optional: start line to focus on")] int? startLine = null,
        [Description("Optional: end line to focus on")] int? endLine = null,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
            return [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = $"File not found in workspace: {filePath}" } }];

        var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        var symbolsSummary = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });

        string codeSection;
        if (startLine.HasValue && endLine.HasValue)
        {
            var lines = sourceText.Split('\n');
            var start = Math.Max(0, startLine.Value - 1);
            var end = Math.Min(lines.Length, endLine.Value);
            codeSection = string.Join('\n', lines[start..end].Select((l, i) => $"{start + i + 1,4}: {l.TrimEnd('\r')}"));
        }
        else
        {
            codeSection = sourceText;
        }

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Analyze the following C# code and suggest refactorings to improve its quality, readability, and maintainability.

                        **File:** {filePath}

                        **Document Symbols:**
                        ```json
                        {symbolsSummary}
                        ```

                        **Code:**
                        ```csharp
                        {codeSection}
                        ```

                        Please suggest refactorings considering:
                        1. SOLID principles violations
                        2. Code duplication opportunities
                        3. Method extraction candidates
                        4. Naming improvements
                        5. Pattern usage (e.g., Strategy, Factory, Builder)
                        6. Performance improvements
                        7. C# idiom improvements (pattern matching, LINQ, etc.)

                        For each suggestion, provide the specific code change and explain the benefit.
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "review_file")]
    [Description("Generate a prompt to perform a code review on a file")]
    public static async Task<IEnumerable<PromptMessage>> ReviewFile(
        IWorkspaceManager workspace,
        ISymbolService symbolService,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
            return [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = $"File not found in workspace: {filePath}" } }];

        var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, null, ct).ConfigureAwait(false);

        var symbolsSummary = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });
        var diagnosticsSummary = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Perform a thorough code review of the following C# source file.

                        **File:** {filePath}

                        **Document Symbols:**
                        ```json
                        {symbolsSummary}
                        ```

                        **Current Diagnostics:**
                        ```json
                        {diagnosticsSummary}
                        ```

                        **Source Code:**
                        ```csharp
                        {sourceText}
                        ```

                        Review the code for:
                        1. **Correctness**: Logic errors, edge cases, null handling
                        2. **Security**: Injection risks, input validation, sensitive data exposure
                        3. **Performance**: Unnecessary allocations, N+1 queries, missing async/await
                        4. **Thread Safety**: Race conditions, shared mutable state
                        5. **Design**: SOLID violations, coupling issues, missing abstractions
                        6. **Maintainability**: Readability, naming, documentation gaps
                        7. **Error Handling**: Missing try/catch, swallowed exceptions
                        8. **Testing**: Testability concerns, missing validation

                        For each issue found, specify the line number, severity (critical/major/minor/suggestion), and proposed fix.
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "analyze_dependencies")]
    [Description("Generate a prompt to analyze architecture and dependency structure of a workspace")]
    public static async Task<IEnumerable<PromptMessage>> AnalyzeDependencies(
        IWorkspaceManager workspace,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default)
    {
        var graph = workspace.GetProjectGraph(workspaceId);
        var graphJson = JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true });

        var namespaceDeps = await analysisService.GetNamespaceDependenciesAsync(workspaceId, null, ct).ConfigureAwait(false);
        var namespaceDepsJson = JsonSerializer.Serialize(namespaceDeps, new JsonSerializerOptions { WriteIndented = true });

        var nugetDeps = await analysisService.GetNuGetDependenciesAsync(workspaceId, ct).ConfigureAwait(false);
        var nugetDepsJson = JsonSerializer.Serialize(nugetDeps, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Analyze the architecture and dependency structure of this .NET solution.

                        **Project Dependency Graph:**
                        ```json
                        {graphJson}
                        ```

                        **Namespace Dependencies:**
                        ```json
                        {namespaceDepsJson}
                        ```

                        **NuGet Dependencies:**
                        ```json
                        {nugetDepsJson}
                        ```

                        Please analyze:
                        1. **Architecture**: Identify the layering strategy and assess if it follows clean architecture / onion architecture principles
                        2. **Circular Dependencies**: Flag any circular namespace or project dependencies and suggest how to break them
                        3. **Coupling**: Identify tightly coupled components and suggest decoupling strategies
                        4. **NuGet Health**: Flag outdated, redundant, or conflicting package versions
                        5. **Dependency Direction**: Verify that dependencies flow in the correct direction (e.g., UI → Domain, not Domain → UI)
                        6. **Modularity**: Suggest opportunities to extract shared libraries or consolidate projects
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "debug_test_failure")]
    [Description("Generate a prompt to diagnose test failures by running tests and analyzing the output")]
    public static async Task<IEnumerable<PromptMessage>> DebugTestFailure(
        IValidationService validationService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        [Description("Optional: test filter expression to narrow which tests to run")] string? filter = null,
        CancellationToken ct = default)
    {
        var testResult = await validationService.RunTestsAsync(workspaceId, projectName, filter, ct).ConfigureAwait(false);
        var testResultJson = JsonSerializer.Serialize(testResult, new JsonSerializerOptions { WriteIndented = true });

        var failureSummary = testResult.Failures.Count > 0
            ? string.Join("\n", testResult.Failures.Select((f, i) =>
                $"  {i + 1}. **{f.DisplayName}**\n     Message: {f.Message}\n     Stack: {f.StackTrace ?? "N/A"}"))
            : "No failures detected.";

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Diagnose the following test failure(s) and suggest fixes.

                        **Test Summary:** {testResult.Total} total, {testResult.Passed} passed, {testResult.Failed} failed, {testResult.Skipped} skipped

                        **Test Results (full):**
                        ```json
                        {testResultJson}
                        ```

                        **Failure Details:**
                        {failureSummary}

                        Please:
                        1. Identify the root cause of each failing test
                        2. Determine if the failure is in the test itself or the code under test
                        3. Suggest specific code changes to fix the failure
                        4. If the test expectation is wrong, explain why and suggest the correct assertion
                        5. Note any test isolation issues (shared state, timing, external dependencies)
                        """
                }
            }
        ];
    }
}
