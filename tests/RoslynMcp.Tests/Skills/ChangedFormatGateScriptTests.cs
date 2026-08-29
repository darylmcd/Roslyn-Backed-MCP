using System.Diagnostics;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// format-changed-file-gate: behavior tests for <c>eng/verify-changed-format.ps1</c>.
///
/// The gate exists because the repository carries a large tracked formatter baseline
/// (<c>eng/format-baseline.json</c>) that makes a whole-repo <c>dotnet format --verify-no-changes</c>
/// gate impossible. It therefore verifies only the files a change touches, and only the findings
/// those files did not already carry in the baseline.
///
/// HARD INVARIANT: every test seeds an isolated temp git repository under
/// <see cref="TestTempRoot.Current"/> containing its own project, <c>.editorconfig</c>, and
/// baseline inventory, then shells the REAL script against it. Nothing here reads or mutates the
/// production repository. Pattern mirrors <c>AggregatePromotionScorecardsScriptTests.cs</c>.
///
/// The tests deliberately drive real <c>dotnet format</c> rather than a stubbed parser: the whole
/// value of the gate is that the diagnostic ids it classifies are the ids the formatter actually
/// emits, which a stub would silently stop proving the moment the SDK changed its wording.
/// </summary>
[TestClass]
public sealed class ChangedFormatGateScriptTests
{
    private const string BaseBranchName = "gate-base";

    private const string ProjectFileName = "Probe.csproj";

    /// <summary>
    /// A file carrying exactly one <c>IDE1006</c> finding, mirrored by <see cref="BaselineWithOneDirtyIde1006"/>.
    /// Used as the "already in the inventory" fixture for the tracked-debt and concealment cases.
    /// </summary>
    private static string DirtyFileWithOneNamingViolation =>
        "namespace Probe;\n\npublic sealed class Dirty\n{\n    private int badField;\n\n    public int Value => badField;\n}\n";

    private static string CleanFile =>
        "namespace Probe;\n\npublic sealed class Clean\n{\n    public int Value => 1;\n}\n";

    private static string BaselineWithOneDirtyIde1006 => """
        {
          "schemaVersion": 1,
          "command": "dotnet format Probe.csproj --verify-no-changes --no-restore",
          "diagnosticIds": [ "IDE1006" ],
          "totals": {
            "findingCount": 1,
            "fileCount": 1,
            "countsByDiagnosticId": { "IDE1006": 1 }
          },
          "files": [
            {
              "path": "Dirty.cs",
              "findingCount": 1,
              "diagnosticIds": [ "IDE1006" ],
              "countsByDiagnosticId": { "IDE1006": 1 }
            }
          ]
        }
        """;

    private static string EmptyBaseline => """
        {
          "schemaVersion": 1,
          "command": "dotnet format Probe.csproj --verify-no-changes --no-restore",
          "diagnosticIds": [],
          "totals": { "findingCount": 0, "fileCount": 0, "countsByDiagnosticId": {} },
          "files": []
        }
        """;

    private string _repositoryDirectory = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _repositoryDirectory = Path.Combine(TestTempRoot.Current, "ChangedFormatGate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repositoryDirectory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_repositoryDirectory);
    }

    [TestMethod]
    public void Script_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"verify-changed-format.ps1 was not found at the documented path '{scriptPath}'. " +
            "CI_POLICY.md, the justfile `verify-changed-format` recipe, and the ci.yml validate leg " +
            "all reference this exact path; without the file every one of them is a dead reference.");
    }

    [TestMethod]
    public void Gate_NoChangedCSharpFiles_PassesWithAnExplicitNoOp()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        // A commit that touches no C# file at all. The gate must say so explicitly rather than
        // silently reporting success, so a misconfigured base ref cannot look like a green run.
        WriteRepositoryFile("README.md", "# probe\n");
        CommitAll("docs only");

        var result = RunGate();

        Assert.AreEqual(0, result.ExitCode, Describe("A change with no C# files must pass", result));
        StringAssert.Contains(
            result.StdOut,
            "no changed C# files",
            Describe("The no-op path must be reported explicitly", result));
    }

    [TestMethod]
    public void Gate_ChangedFileIsClean_Passes()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        WriteRepositoryFile("Clean.cs", "namespace Probe;\n\npublic sealed class Clean\n{\n    public int Value => 42;\n}\n");
        CommitAll("clean edit");

        var result = RunGate();

        Assert.AreEqual(0, result.ExitCode, Describe("A formatter-clean changed file must pass", result));
        StringAssert.Contains(result.StdOut, "0 new finding(s)", Describe("The pass summary must report zero new findings", result));
    }

    [TestMethod]
    public void Gate_TrackedStagedAndUntrackedLocalCSharpChanges_AreAllGated()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        WriteRepositoryFile("Clean.cs", CleanFile.Replace("=> 1", "=> 2", StringComparison.Ordinal));
        WriteRepositoryFile("Staged.cs", CleanFile.Replace("class Clean", "class Staged", StringComparison.Ordinal));
        RunGit("add", "Staged.cs");
        WriteRepositoryFile("Untracked.cs", CleanFile.Replace("class Clean", "class Untracked", StringComparison.Ordinal));

        var result = RunGateWithFakeDotnet(
            string.Join(
                "\n",
                $"{Path.Combine(_repositoryDirectory, "Clean.cs")}(1,1): warning IDE1006: tracked local change [Probe.csproj]",
                $"{Path.Combine(_repositoryDirectory, "Staged.cs")}(1,1): warning IDE1006: staged local change [Probe.csproj]",
                $"{Path.Combine(_repositoryDirectory, "Untracked.cs")}(1,1): warning IDE1006: untracked local change [Probe.csproj]"),
            exitCode: 2);

        Assert.AreEqual(1, result.ExitCode, Describe("Every local C# change state must be formatter-gated before commit", result));
        StringAssert.Contains(result.StdErr, "Clean.cs", Describe("The tracked worktree change must be gated", result));
        StringAssert.Contains(result.StdErr, "Staged.cs", Describe("The staged addition must be gated", result));
        StringAssert.Contains(result.StdErr, "Untracked.cs", Describe("The untracked addition must be gated", result));
    }

    [TestMethod]
    public void Gate_ChangedFileIntroducesUnsortedUsings_FailsWithAnImportsFinding()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        // `System.Text` before `System` violates dotnet_sort_system_directives_first -> IMPORTS.
        WriteRepositoryFile(
            "Clean.cs",
            "using System.Text;\nusing System;\n\nnamespace Probe;\n\npublic sealed class Clean\n{\n" +
            "    public int Value => new StringBuilder().Length + Console.In.GetHashCode();\n}\n");
        CommitAll("unsorted usings");

        var result = RunGate();

        Assert.AreEqual(1, result.ExitCode, Describe("A newly introduced IMPORTS finding must fail the gate", result));
        StringAssert.Contains(result.StdErr, "Clean.cs", Describe("The failure must name the offending file", result));
        StringAssert.Contains(result.StdErr, "IMPORTS", Describe("The failure must name the diagnostic id", result));
    }

    [TestMethod]
    public void Gate_ChangedFileIntroducesNamingViolation_FailsWithAnIde1006Finding()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        // A private field without the required `_` prefix -> IDE1006.
        WriteRepositoryFile(
            "Clean.cs",
            "namespace Probe;\n\npublic sealed class Clean\n{\n    private int newlyBadField;\n\n" +
            "    public int Value => newlyBadField;\n}\n");
        CommitAll("naming violation");

        var result = RunGate();

        Assert.AreEqual(1, result.ExitCode, Describe("A newly introduced IDE1006 finding must fail the gate", result));
        StringAssert.Contains(result.StdErr, "Clean.cs", Describe("The failure must name the offending file", result));
        StringAssert.Contains(result.StdErr, "IDE1006", Describe("The failure must name the diagnostic id", result));
    }

    [TestMethod]
    public void Gate_PascalCaseConstantsAndStaticReadonlyFields_Pass()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        WriteRepositoryFile(
            "Clean.cs",
            "using System;\n\nnamespace Probe;\n\npublic sealed class Clean\n{\n" +
            "    private const int MaxItems = 2;\n" +
            "    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);\n\n" +
            "    public int Value => MaxItems + DefaultTimeout.Seconds;\n}\n");
        CommitAll("use repository constant naming convention");

        var result = RunGate();

        Assert.AreEqual(0, result.ExitCode, Describe("PascalCase constants and static readonly fields are exempt", result));
        StringAssert.Contains(result.StdOut, "0 new finding(s)", Describe("The exemption must not produce IDE1006", result));
    }

    [TestMethod]
    public void Gate_ChangedFileIsMissingItsFinalNewline_FailsWithAFinalNewlineFinding()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));

        WriteRepositoryFile("Clean.cs", "namespace Probe;\n\npublic sealed class Clean\n{\n    public int Value => 7;\n}");
        CommitAll("missing final newline");

        var result = RunGate();

        Assert.AreEqual(1, result.ExitCode, Describe("A missing final newline must fail the gate", result));
        StringAssert.Contains(result.StdErr, "Clean.cs", Describe("The failure must name the offending file", result));
        StringAssert.Contains(result.StdErr, "FINALNEWLINE", Describe("The failure must name the diagnostic id", result));
    }

    [TestMethod]
    public void Gate_ChangedFileCarriesOnlyInventoriedDebt_PassesAndReportsItAsTrackedRatherThanSuppressed()
    {
        SeedBaseCommit(
            BaselineWithOneDirtyIde1006,
            ("Dirty.cs", DirtyFileWithOneNamingViolation),
            ("Clean.cs", CleanFile));

        // Touch the inventoried file without adding any new formatter debt: its single baseline
        // IDE1006 travels along unchanged. That is tracked debt, not a regression.
        WriteRepositoryFile(
            "Dirty.cs",
            "namespace Probe;\n\npublic sealed class Dirty\n{\n    private int badField;\n\n" +
            "    public int Value => badField;\n\n    public int Doubled => Value * 2;\n}\n");
        CommitAll("edit an inventoried file without adding debt");

        var result = RunGate();

        Assert.AreEqual(0, result.ExitCode, Describe("Inventoried debt on a changed file must not fail the gate", result));
        StringAssert.Contains(
            result.StdOut,
            "tracked debt: Dirty.cs - IDE1006 x1",
            Describe("Baseline debt must be reported, not silently suppressed", result));
        StringAssert.Contains(result.StdOut, "0 new finding(s)", Describe("No new findings were introduced", result));
    }

    [TestMethod]
    public void Gate_ChangedFileCarriesBaselineDebtPlusOneNewViolation_StillFails()
    {
        // The concealment case. `Dirty.cs` is in the inventory with ONE IDE1006. If the gate
        // classified findings by mere presence in the baseline, a second, freshly introduced
        // IDE1006 in the same file would be masked by the first and ship silently. The gate
        // compares COUNTS, so observed(2) > baseline(1) is a failure.
        SeedBaseCommit(
            BaselineWithOneDirtyIde1006,
            ("Dirty.cs", DirtyFileWithOneNamingViolation),
            ("Clean.cs", CleanFile));

        WriteRepositoryFile(
            "Dirty.cs",
            "namespace Probe;\n\npublic sealed class Dirty\n{\n    private int badField;\n    private int smuggledField;\n\n" +
            "    public int Value => badField + smuggledField;\n}\n");
        CommitAll("smuggle a second naming violation into an inventoried file");

        var result = RunGate();

        Assert.AreEqual(
            1,
            result.ExitCode,
            Describe("A baseline entry must not mask a newly introduced finding in the same file", result));
        StringAssert.Contains(
            result.StdErr,
            "NEW: Dirty.cs - IDE1006 x1 (observed 2, baseline 1)",
            Describe("The failure must show the observed-vs-baseline count that proves concealment", result));
    }

    [TestMethod]
    public void Gate_NonFormatterDiagnosticOnChangedFile_IsIgnored()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));
        WriteRepositoryFile("Clean.cs", CleanFile.Replace("=> 1", "=> 2", StringComparison.Ordinal));
        CommitAll("clean edit with unrelated diagnostic output");

        var result = RunGateWithFakeDotnet(
            $"{Path.Combine(_repositoryDirectory, "Clean.cs")}(1,1): error CS9999: compiler diagnostic [Probe.csproj]",
            exitCode: 2);

        Assert.AreEqual(0, result.ExitCode, Describe("Compiler diagnostics are owned by the build gate", result));
        StringAssert.Contains(result.StdOut, "0 new finding(s)", Describe("No formatter finding was introduced", result));
    }

    [TestMethod]
    public void Gate_MissingBaseRef_FailsWithFetchGuidance()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));
        WriteRepositoryFile("Clean.cs", CleanFile.Replace("=> 1", "=> 2", StringComparison.Ordinal));
        CommitAll("clean edit");

        var result = RunGate(baseRef: "missing-base-ref");

        Assert.AreNotEqual(0, result.ExitCode, Describe("An unresolvable base ref must fail closed", result));
        StringAssert.Contains(result.StdErr, "Fetch the base ref", Describe("The failure must carry corrective guidance", result));
    }

    [TestMethod]
    public void Gate_TruncatedFormatterReport_FailsClosed()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));
        WriteRepositoryFile("Clean.cs", CleanFile.Replace("=> 1", "=> 2", StringComparison.Ordinal));
        CommitAll("clean edit");

        var result = RunGateWithFakeDotnet("Required references did not load for Probe.csproj", exitCode: 0);

        Assert.AreNotEqual(0, result.ExitCode, Describe("A truncated formatter report must fail closed", result));
        StringAssert.Contains(result.StdErr, "truncated report", Describe("The failure must identify report truncation", result));
    }

    [TestMethod]
    public void Gate_UnexpectedFormatterExitCode_FailsClosed()
    {
        SeedBaseCommit(EmptyBaseline, ("Clean.cs", CleanFile));
        WriteRepositoryFile("Clean.cs", CleanFile.Replace("=> 1", "=> 2", StringComparison.Ordinal));
        CommitAll("clean edit");

        var result = RunGateWithFakeDotnet("formatter process failed", exitCode: 7);

        Assert.AreNotEqual(0, result.ExitCode, Describe("An unexpected formatter exit must fail closed", result));
        StringAssert.Contains(result.StdErr, "unexpected code 7", Describe("The failure must preserve the exit code", result));
    }

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "eng", "verify-changed-format.ps1");
    }

    /// <summary>
    /// Builds the synthetic repository (project, editorconfig, baseline, seed sources), commits it,
    /// and parks <see cref="BaseBranchName"/> on that commit so later commits are the "changed set".
    /// </summary>
    private void SeedBaseCommit(string baselineJson, params (string Name, string Content)[] sourceFiles)
    {
        WriteRepositoryFile(
            ProjectFileName,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");

        // `root = true` stops inheritance from anything above the temp directory, and the rules
        // below are the exact ones the production .editorconfig uses for the gated diagnostics.
        WriteRepositoryFile(
            ".editorconfig",
            "root = true\n\n[*]\nindent_style = space\nindent_size = 4\nend_of_line = lf\n" +
            "trim_trailing_whitespace = true\ninsert_final_newline = true\n\n[*.cs]\n" +
            "dotnet_sort_system_directives_first = true\ndotnet_separate_import_directive_groups = false\n" +
            "dotnet_naming_rule.private_fields_should_be_camel_case.severity = warning\n" +
            "dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields\n" +
            "dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore\n" +
            "dotnet_naming_rule.private_constants_are_exempt.severity = none\n" +
            "dotnet_naming_rule.private_constants_are_exempt.symbols = private_constants\n" +
            "dotnet_naming_rule.private_constants_are_exempt.style = pascal_case\n" +
            "dotnet_naming_rule.private_static_readonly_fields_are_exempt.severity = none\n" +
            "dotnet_naming_rule.private_static_readonly_fields_are_exempt.symbols = private_static_readonly_fields\n" +
            "dotnet_naming_rule.private_static_readonly_fields_are_exempt.style = pascal_case\n" +
            "dotnet_naming_symbols.private_constants.applicable_kinds = field\n" +
            "dotnet_naming_symbols.private_constants.applicable_accessibilities = private\n" +
            "dotnet_naming_symbols.private_constants.required_modifiers = const\n" +
            "dotnet_naming_symbols.private_static_readonly_fields.applicable_kinds = field\n" +
            "dotnet_naming_symbols.private_static_readonly_fields.applicable_accessibilities = private\n" +
            "dotnet_naming_symbols.private_static_readonly_fields.required_modifiers = static, readonly\n" +
            "dotnet_naming_symbols.private_fields.applicable_kinds = field\n" +
            "dotnet_naming_symbols.private_fields.applicable_accessibilities = private\n" +
            "dotnet_naming_style.pascal_case.capitalization = pascal_case\n" +
            "dotnet_naming_style.camel_case_underscore.required_prefix = _\n" +
            "dotnet_naming_style.camel_case_underscore.capitalization = camel_case\n");

        // `* -text` disables git's newline translation, so a checked-out fixture file is byte-identical
        // to what the test wrote and cannot acquire CRLF-shaped formatter findings the test never intended.
        WriteRepositoryFile(".gitattributes", "* -text\n");
        WriteRepositoryFile(".gitignore", "bin/\nobj/\n");
        WriteRepositoryFile(Path.Combine("eng", "format-baseline.json"), baselineJson);

        foreach (var (name, content) in sourceFiles)
        {
            WriteRepositoryFile(name, content);
        }

        RunGit("init", "--quiet", "--initial-branch=main", ".");
        CommitAll("base");
        RunGit("branch", BaseBranchName);
    }

    private void WriteRepositoryFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_repositoryDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private void CommitAll(string message)
    {
        RunGit("add", "--all");
        RunGit("-c", "user.email=gate@test.invalid", "-c", "user.name=gate-test", "commit", "--quiet", "--message", message);
    }

    private void RunGit(params string[] arguments)
    {
        var result = RunProcess("git", _repositoryDirectory, arguments);
        Assert.AreEqual(
            0,
            result.ExitCode,
            $"git {string.Join(' ', arguments)} failed in the fixture repository. stdout={result.StdOut} stderr={result.StdErr}");
    }

    private ProcessResult RunGate(string? baseRef = null, string? dotnetCommand = null)
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(File.Exists(scriptPath), $"verify-changed-format.ps1 was not found at '{scriptPath}'.");

        var arguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File",
            scriptPath,
            "-BaseRef",
            baseRef ?? BaseBranchName,
            "-SolutionPath",
            ProjectFileName,
        };
        if (dotnetCommand is not null)
        {
            arguments.Add("-NoRestore");
            arguments.Add("-DotnetCommand");
            arguments.Add(dotnetCommand);
        }

        return RunProcess(
            OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            _repositoryDirectory,
            [.. arguments]);
    }

    private ProcessResult RunGateWithFakeDotnet(string output, int exitCode)
    {
        var fakeDotnetPath = Path.Combine(_repositoryDirectory, "fake-dotnet.ps1");
        var outputCommands = string.Join(
            "\n",
            output.Split('\n').Select(line => $"Write-Output '{line.TrimEnd('\r').Replace("'", "''", StringComparison.Ordinal)}'"));
        WriteRepositoryFile(
            "fake-dotnet.ps1",
            $"param([Parameter(ValueFromRemainingArguments = $true)][string[]] $RemainingArguments)\n{outputCommands}\nexit {exitCode}\n");
        return RunGate(dotnetCommand: fakeDotnetPath);
    }

    private static ProcessResult RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 300_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{fileName} {string.Join(' ', arguments)}' timed out after 300s.");
        }

        return new ProcessResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private static string Describe(string expectation, ProcessResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{expectation}. exit={result.ExitCode}\n--- stdout ---\n{result.StdOut}\n--- stderr ---\n{result.StdErr}");

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
