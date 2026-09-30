using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for <c>set-editorconfig-option-round-trip-asymmetry</c>:
/// <see cref="IEditorConfigService.SetOptionAsync"/> writes to
/// <c>[*.{cs,csx,cake}]</c> but the pre-fix section matcher in
/// <see cref="IEditorConfigService.GetOptionsAsync"/>'s on-disk supplement
/// did not recognize brace-expansion glob sections, so keys for unloaded
/// analyzer ids (e.g. <c>CA9999</c>) were silently dropped on the following read.
/// </summary>
[TestClass]
public sealed class EditorConfigServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SetOptionAsync_RejectsUnloadedSourceBeforeCreatingConfig()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var unrelated = Path.Combine(workspace.RootPath, "Unloaded", "Missing.cs");
        var config = Path.Combine(workspace.RootPath, ".editorconfig");
        var before = File.Exists(config) ? await File.ReadAllBytesAsync(config) : null;

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            EditorConfigService.SetOptionAsync(workspace.WorkspaceId, unrelated,
                "indent_size", "8", "set_editorconfig_option", CancellationToken.None));

        var after = File.Exists(config) ? await File.ReadAllBytesAsync(config) : null;
        if (before is null)
        {
            Assert.IsNull(after, "A rejected source must not create .editorconfig.");
        }
        else
        {
            CollectionAssert.AreEqual(before, after!, "A rejected source must not modify .editorconfig.");
        }
        Assert.IsFalse(WorkspaceManager.GetStatus(workspace.WorkspaceId).IsStale);
    }

    [TestMethod]
    public async Task SetOptionAsync_RejectsSourceOutsideLoadedWorkspaceBeforeFileIo()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetDirectoryName(workspace.RootPath)!,
            $"unloaded-{Guid.NewGuid():N}");
        var source = Path.Combine(outside, "Outside.cs");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            EditorConfigService.SetOptionAsync(workspace.WorkspaceId, source,
                "indent_size", "8", "set_editorconfig_option", CancellationToken.None));

        Assert.IsFalse(Directory.Exists(outside), "A refused path must not create an outside directory.");
        Assert.IsFalse(WorkspaceManager.GetStatus(workspace.WorkspaceId).IsStale);
    }

    [TestMethod]
    public async Task SetOptionAsync_RefusesApplicableAncestorConfigOutsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"roslyn-config-ancestor-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "Probe.csproj");
        var source = Path.Combine(projectRoot, "Probe.cs");
        var ancestorConfig = Path.Combine(root, ".editorconfig");
        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(source, "namespace Probe; public class ProbeType { }\n");
        await File.WriteAllTextAsync(ancestorConfig, "[*.cs]\nindent_size = 4\n");
        var original = await File.ReadAllBytesAsync(ancestorConfig);
        string? workspaceId = null;
        try
        {
            workspaceId = (await WorkspaceManager.LoadAsync(projectPath, CancellationToken.None)).WorkspaceId;
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                EditorConfigService.SetOptionAsync(workspaceId, source, "indent_size", "8",
                    "set_editorconfig_option", CancellationToken.None));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(ancestorConfig));
            Assert.IsFalse(WorkspaceManager.GetStatus(workspaceId).IsStale);
        }
        finally
        {
            if (workspaceId is not null) WorkspaceManager.Close(workspaceId);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SetOptionAsync_RefusesLoadedLinkedDocumentOutsidePhysicalWorkspace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var outside = Path.Combine(Path.GetDirectoryName(workspace.RootPath)!,
            $"linked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var linkedSource = Path.Combine(outside, "Linked.cs");
        await File.WriteAllTextAsync(linkedSource, "namespace SampleLib; public class LinkedType { }\n");
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var relative = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, linkedSource);
        var projectXml = await File.ReadAllTextAsync(projectPath);
        projectXml = projectXml.Replace("</Project>",
            $"<ItemGroup><Compile Include=\"{relative}\" Link=\"Linked.cs\" /></ItemGroup></Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectPath, projectXml);

        try
        {
            var workspaceId = await workspace.LoadAsync();
            Assert.IsTrue(WorkspaceManager.GetCurrentSolution(workspaceId).Projects
                .SelectMany(project => project.Documents)
                .Any(document => string.Equals(document.FilePath, linkedSource,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                EditorConfigService.SetOptionAsync(workspaceId, linkedSource, "indent_size", "8",
                    "set_editorconfig_option", CancellationToken.None));
            Assert.IsFalse(File.Exists(Path.Combine(outside, ".editorconfig")));
        }
        finally
        {
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [TestMethod]
    public async Task SetOptionAsync_AlternateWorkspaceManagerWithoutCoordinator_RefusesWrite()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var proxy = System.Reflection.DispatchProxy.Create<
            RoslynMcp.Roslyn.Contracts.IWorkspaceManager, ForwardingWorkspaceManagerProxy>();
        ((ForwardingWorkspaceManagerProxy)(object)proxy).Target = WorkspaceManager;
        var service = new EditorConfigService(proxy);
        var source = workspace.GetPath("SampleLib", "Dog.cs");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.SetOptionAsync(workspace.WorkspaceId, source, "indent_size", "8",
                "set_editorconfig_option", CancellationToken.None));
        StringAssert.Contains(exception.Message, "coordinated file watcher");
        Assert.IsFalse(WorkspaceManager.GetStatus(workspace.WorkspaceId).IsStale);
    }

    public class ForwardingWorkspaceManagerProxy : System.Reflection.DispatchProxy
    {
        public RoslynMcp.Roslyn.Contracts.IWorkspaceManager Target { get; set; } = null!;

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Invoke(Target, args);
    }

    [TestMethod]
    public async Task SetOptionAsync_MarksOwnedConfigWriteAsApply()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");

        await EditorConfigService.SetOptionAsync(workspace.WorkspaceId, source,
            "dotnet_diagnostic.CA1861.severity", "none", "set_editorconfig_option", CancellationToken.None);

        var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
        Assert.IsTrue(status.IsStale);
        Assert.AreEqual(RoslynMcp.Core.Services.StaleReasons.Apply, status.StaleReason);
        // Let the queued FileSystemWatcher event run; it must not relabel these same bytes.
        await Task.Delay(300);
        Assert.AreEqual(RoslynMcp.Core.Services.StaleReasons.Apply,
            WorkspaceManager.GetStatus(workspace.WorkspaceId).StaleReason);
    }

    [TestMethod]
    public async Task SetOptionAsync_FailedPostWriteCheck_PreservesPriorUndoAndChangeHistory()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var configPath = workspace.GetPath("SampleLib", ".editorconfig");
        var original = "[*.cs]\nindent_size = 4\n";
        await File.WriteAllTextAsync(configPath, original);

        await EditorConfigService.SetOptionAsync(workspace.WorkspaceId, source,
            "indent_size", "8", "set_editorconfig_option", CancellationToken.None);
        var firstUndo = UndoService.GetLastOperation(workspace.WorkspaceId);
        Assert.IsNotNull(firstUndo);
        var firstBytes = await File.ReadAllBytesAsync(configPath);
        var firstChanges = ChangeTracker.GetChanges(workspace.WorkspaceId).Count;

        var failingService = new EditorConfigService(WorkspaceManager, UndoService, ChangeTracker,
            logger: null, new RejectAfterWriteCoordinator());
        await Assert.ThrowsExactlyAsync<EditorConfigConcurrentEditException>(() =>
            failingService.SetOptionAsync(workspace.WorkspaceId, source, "indent_size", "2",
                "set_editorconfig_option", CancellationToken.None));

        CollectionAssert.AreEqual(firstBytes, await File.ReadAllBytesAsync(configPath));
        Assert.AreEqual(firstUndo, UndoService.GetLastOperation(workspace.WorkspaceId));
        Assert.AreEqual(firstChanges, ChangeTracker.GetChanges(workspace.WorkspaceId).Count);
        Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId));
        Assert.AreEqual(original, await File.ReadAllTextAsync(configPath));
    }

    private sealed class RejectAfterWriteCoordinator : IEditorConfigWriteCoordinator
    {
        public T RunOwnedWrite<T>(string workspaceId, string path, Func<EditorConfigFileTransaction, T> write)
        {
            var originalBytes = File.ReadAllBytes(path);
            using (var transaction = new EditorConfigFileTransaction(path, originalBytes))
            {
                _ = write(transaction);
            }
            File.WriteAllBytes(path, originalBytes);
            throw new EditorConfigConcurrentEditException();
        }
    }

    [TestMethod]
    public async Task SetOptionAsync_UnrecoveredFailedWrite_CapturesUndoAndChange()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var configPath = workspace.GetPath("SampleLib", ".editorconfig");
        var original = "[*.cs]\nindent_size = 4\n";
        await File.WriteAllTextAsync(configPath, original);
        var priorChanges = ChangeTracker.GetChanges(workspace.WorkspaceId).Count;

        var failingService = new EditorConfigService(WorkspaceManager, UndoService, ChangeTracker,
            logger: null, new UnrecoveredAfterWriteCoordinator());
        await Assert.ThrowsExactlyAsync<EditorConfigUnrecoveredWriteException>(() =>
            failingService.SetOptionAsync(workspace.WorkspaceId, source, "indent_size", "8",
                "set_editorconfig_option", CancellationToken.None));

        Assert.AreNotEqual(original, await File.ReadAllTextAsync(configPath));
        StringAssert.Contains(UndoService.GetLastOperation(workspace.WorkspaceId)?.Description,
            "Failed .editorconfig write");
        Assert.AreEqual(priorChanges + 1, ChangeTracker.GetChanges(workspace.WorkspaceId).Count);
        Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId));
        Assert.AreEqual(original, await File.ReadAllTextAsync(configPath));
    }

    private sealed class UnrecoveredAfterWriteCoordinator : IEditorConfigWriteCoordinator
    {
        public T RunOwnedWrite<T>(string workspaceId, string path, Func<EditorConfigFileTransaction, T> write)
        {
            var originalBytes = File.ReadAllBytes(path);
            using var transaction = new EditorConfigFileTransaction(path, originalBytes);
            _ = write(transaction);
            throw new EditorConfigUnrecoveredWriteException(new IOException("simulated failed restoration"));
        }
    }

    [TestMethod]
    public async Task ParseEditorconfigCsKeys_ClosesReadHandleBeforeYielding()
    {
        if (!OperatingSystem.IsWindows()) return;

        var configPath = Path.Combine(Path.GetTempPath(), $"roslyn-config-read-{Guid.NewGuid():N}.editorconfig");
        await File.WriteAllTextAsync(configPath, "[*.cs]\nindent_size = 4\n");
        try
        {
            using var entries = EditorConfigService.ParseEditorconfigCsKeys(configPath).GetEnumerator();
            Assert.IsTrue(entries.MoveNext());
            Assert.AreEqual("4", entries.Current.Value);
            await AtomicFileWriter.WriteAllTextAsync(configPath, "[*.cs]\nindent_size = 8\n", CancellationToken.None);
            Assert.AreEqual("[*.cs]\nindent_size = 8\n", await File.ReadAllTextAsync(configPath));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [TestMethod]
    public async Task SetDiagnosticSeverity_RefreshesGatedDiagnosticsWithoutManualReload()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        await File.AppendAllTextAsync(source,
            "\nnamespace SampleLib { public static class ConfigProbe { " +
            "public static string Join() => string.Join(\",\", new[] { \"a\", \"b\" }); } }\n");
        var workspaceId = await workspace.LoadAsync();

        async Task<int> CountAsync()
        {
            var json = await AnalysisTools.GetProjectDiagnostics(WorkspaceExecutionGate,
                DiagnosticService, workspaceId, projectName: "SampleLib", file: source,
                diagnosticId: "CA1861", summary: true, ct: CancellationToken.None);
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.GetProperty("filteredDiagnostics").GetInt32();
        }

        Assert.IsTrue(await CountAsync() > 0, "CA1861 must be present before the severity change.");
        var versionBefore = WorkspaceManager.GetStatus(workspaceId).WorkspaceVersion;
        var suppression = new SuppressionService(EditorConfigService, EditService);
        var write = await suppression.SetDiagnosticSeverityAsync(workspaceId, "CA1861", "none",
            source, CancellationToken.None);
        Assert.AreEqual(RoslynMcp.Core.Services.StaleReasons.Apply,
            WorkspaceManager.GetStatus(workspaceId).StaleReason);

        var afterCount = await CountAsync();
        Assert.AreEqual(0, afterCount,
            $"Gated diagnostics must reload the new severity. version={WorkspaceManager.GetStatus(workspaceId).WorkspaceVersion}, " +
            $"config={await File.ReadAllTextAsync(write.EditorConfigPath)}");
        Assert.IsTrue(WorkspaceManager.GetStatus(workspaceId).WorkspaceVersion > versionBefore);
    }

    [TestMethod]
    public async Task SetThenGet_UnloadedAnalyzerId_IsReturned()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // CA9999 is not a real analyzer id; no loaded analyzer will report it via
        // Roslyn's AnalyzerConfigOptionsProvider. The key must still come back from
        // the on-disk union supplement.
        const string unloadedKey = "dotnet_diagnostic.CA9999.severity";
        const string value = "none";

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, unloadedKey, value, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(setResult.EditorConfigPath));

        var options = await EditorConfigService.GetOptionsAsync(
            workspaceId, dogFilePath, CancellationToken.None);

        var entry = options.Options.FirstOrDefault(o =>
            string.Equals(o.Key, unloadedKey, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry,
            $"Get must surface '{unloadedKey}' after Set writes it, even when no loaded analyzer reports the id. " +
            $"Returned keys: {string.Join(", ", options.Options.Select(o => o.Key))}");
        Assert.AreEqual(value, entry!.Value);
    }

    /// <summary>
    /// Regression guard for direct-mutation-undo-byte-fidelity: <c>set_editorconfig_option</c>
    /// must capture a byte-exact pre-apply snapshot (via <c>FileSnapshotDto.FromExistingBytes</c>)
    /// of a pre-existing <c>.editorconfig</c> so <c>revert_last_apply</c> restores its original
    /// BOM/encoding exactly, not a re-encoded-as-default-UTF8 approximation.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SetOptionAsync_ThenRevert_RestoresOriginalBytes(bool useUtf16)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string key = "dotnet_diagnostic.CA9877.severity";
        const string originalContent = "[*.{cs,csx,cake}]\ndotnet_diagnostic.CA9877.severity = warning\n";

        Encoding encoding = useUtf16
            ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var originalBytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(originalContent))
            .ToArray();
        await File.WriteAllBytesAsync(editorconfigPath, originalBytes, CancellationToken.None);

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, "error", "set_editorconfig_option", CancellationToken.None);
        Assert.IsFalse(setResult.CreatedNewFile, "File already existed; CreatedNewFile must be false.");

        var afterSet = await File.ReadAllTextAsync(editorconfigPath, CancellationToken.None);
        StringAssert.Contains(afterSet, "error", "Sanity check: the option must have been updated.");

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed.");

        CollectionAssert.AreEqual(
            originalBytes,
            await File.ReadAllBytesAsync(editorconfigPath, CancellationToken.None),
            "Restored .editorconfig must exactly match the pre-apply byte sequence, including its BOM and encoding.");
    }

    /// <summary>
    /// Regression guard for <c>mutation-write-paths-drop-original-encoding</c>: the FORWARD apply
    /// half of the byte-fidelity contract. <c>SetOptionAsync</c> rewrote the file through the
    /// <c>Encoding</c>-less <see cref="File.WriteAllLines(string, IEnumerable{string})"/> overload,
    /// which is UTF-8-no-BOM by definition — so a UTF-8-BOM or UTF-16 <c>.editorconfig</c> lost its
    /// byte-order mark on every set. The sibling revert test above only covers the restore path.
    /// The <c>utf8-nobom</c> row is the inverse guard: a file that had no BOM must not gain one.
    /// </summary>
    [TestMethod]
    [DataRow("utf8-nobom")]
    [DataRow("utf8-bom")]
    [DataRow("utf16-bom")]
    public async Task SetOptionAsync_PreservesOriginalFileEncoding(string encodingKind)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string key = "dotnet_diagnostic.CA9878.severity";
        const string originalContent = "[*.{cs,csx,cake}]\ndotnet_diagnostic.CA9878.severity = warning\n";

        Encoding encoding = encodingKind switch
        {
            "utf16-bom" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var preamble = encoding.GetPreamble();

        await File.WriteAllBytesAsync(
            editorconfigPath,
            preamble.Concat(encoding.GetBytes(originalContent)).ToArray(),
            CancellationToken.None);

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, "error", "set_editorconfig_option", CancellationToken.None);
        Assert.IsFalse(setResult.CreatedNewFile, "File already existed; CreatedNewFile must be false.");

        var afterBytes = await File.ReadAllBytesAsync(editorconfigPath, CancellationToken.None);
        Assert.IsTrue(
            afterBytes.AsSpan().StartsWith(preamble),
            $"set_editorconfig_option must re-emit the original {encodingKind} byte-order mark instead of rewriting the file as UTF-8-no-BOM.");
        if (preamble.Length == 0)
        {
            Assert.IsFalse(
                afterBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()),
                "An .editorconfig that had no BOM must not gain one.");
        }

        StringAssert.Contains(
            encoding.GetString(afterBytes, preamble.Length, afterBytes.Length - preamble.Length),
            "error",
            "Sanity check: the option must be readable back through the original encoding.");
    }

    /// <summary>
    /// Regression test for <c>editorconfig-write-no-auto-invalidation</c>:
    /// <see cref="IEditorConfigService.GetOptionsAsync"/> must return the value that
    /// <see cref="IEditorConfigService.SetOptionAsync"/> just wrote for a <em>known</em>
    /// key (one that Roslyn's <c>AnalyzerConfigOptionsProvider</c> already has in its
    /// cached workspace snapshot) — without requiring a <c>workspace_reload</c> call
    /// in between.
    /// </summary>
    [TestMethod]
    public async Task SetThenGet_KnownKey_ReturnsNewValueWithoutReload()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // indent_size is a well-known key that Roslyn's AnalyzerConfigOptionsProvider
        // enumerates. Writing a new value via SetOptionAsync must be visible on the
        // immediately-following GetOptionsAsync call without an intervening workspace_reload.
        const string knownKey = "indent_size";
        const string newValue = "4";

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, knownKey, newValue, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(setResult.EditorConfigPath));

        // Intentionally NOT calling workspace_reload — that is the bug surface.
        var options = await EditorConfigService.GetOptionsAsync(
            workspaceId, dogFilePath, CancellationToken.None);

        var entry = options.Options.FirstOrDefault(o =>
            string.Equals(o.Key, knownKey, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry,
            $"Get must surface '{knownKey}' after Set writes it. " +
            $"Returned keys: {string.Join(", ", options.Options.Select(o => o.Key))}");
        Assert.AreEqual(newValue, entry!.Value,
            $"Get must return the newly-written value '{newValue}' without a workspace_reload. " +
            $"Actual value returned: '{entry.Value}'. This indicates the Roslyn-cached snapshot " +
            $"was returned instead of the on-disk value.");
    }

    /// <summary>
    /// Regression test for <c>set-editorconfig-option-duplicate-key-append</c> (gh #735):
    /// <see cref="IEditorConfigService.SetOptionAsync"/> previously matched existing keys
    /// with <c>StartsWith(key + " =", ...)</c>, which silently missed the no-space variant
    /// <c>key=value</c> common in hand-edited or IDE-generated <c>.editorconfig</c> files.
    /// When the predicate missed, the writer fell through to <c>Insert</c> and appended
    /// a duplicate key line on every subsequent call. The fix splits each line on the
    /// first <c>=</c> and compares the trimmed key portion case-insensitively, so the
    /// writer always upserts in place regardless of whitespace around <c>=</c>.
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_SecondCallSameKeyValue_NoOp()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        const string key = "dotnet_diagnostic.CA1234.severity";
        const string firstValue = "warning";
        const string secondValue = "error";

        var firstResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, firstValue, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(firstResult.EditorConfigPath));

        // Second call: identical key + value must be an in-place upsert (no duplicate line).
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, firstValue, "set_editorconfig_option", CancellationToken.None);

        var linesAfterIdempotent = await File.ReadAllLinesAsync(firstResult.EditorConfigPath, CancellationToken.None);
        var firstOccurrences = linesAfterIdempotent.Count(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        });
        Assert.AreEqual(1, firstOccurrences,
            $"After two identical SetOptionAsync calls, key '{key}' must appear exactly once. " +
            $"File content:\n{string.Join("\n", linesAfterIdempotent)}");

        // Third call: same key, different value. Still exactly one occurrence; value updated.
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, secondValue, "set_editorconfig_option", CancellationToken.None);

        var linesAfterUpdate = await File.ReadAllLinesAsync(firstResult.EditorConfigPath, CancellationToken.None);
        var matchingLines = linesAfterUpdate.Where(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        Assert.AreEqual(1, matchingLines.Count,
            $"After three SetOptionAsync calls (same key), key '{key}' must still appear exactly once. " +
            $"File content:\n{string.Join("\n", linesAfterUpdate)}");

        // Verify the value was updated to secondValue.
        var updatedLine = matchingLines[0].Trim();
        var updatedEqIdx = updatedLine.IndexOf('=');
        var updatedValue = updatedLine[(updatedEqIdx + 1)..].Trim();
        Assert.AreEqual(secondValue, updatedValue,
            $"Third call must have updated the value to '{secondValue}'. Actual: '{updatedValue}'.");
    }

    /// <summary>
    /// Regression test for <c>set-editorconfig-option-duplicate-key-append</c> (gh #735):
    /// hand-edited or IDE-generated <c>.editorconfig</c> files commonly write keys as
    /// <c>key=value</c> with no surrounding whitespace. The pre-fix matcher used
    /// <c>StartsWith(key + " =", ...)</c> and missed this variant, so a subsequent
    /// <c>SetOptionAsync</c> call would append a duplicate. After the fix, the writer
    /// splits each line on the first <c>=</c> and recognizes the existing key,
    /// replacing it in place with the canonical <c>key = value</c> format.
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_NoSpaceVariantOnDisk_ReplacedInPlace()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Pre-seed an .editorconfig with the [*.{cs,csx,cake}] section and a no-space
        // entry that mirrors what a hand-edit or IDE generator typically produces.
        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string key = "dotnet_diagnostic.CA9876.severity";
        await File.WriteAllLinesAsync(editorconfigPath,
            ["[*.{cs,csx,cake}]", $"{key}=warning"], CancellationToken.None);

        // SetOptionAsync must recognize the existing no-space entry and replace it in place.
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, "error", "set_editorconfig_option", CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(editorconfigPath, CancellationToken.None);
        var matchingLines = lines.Where(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        Assert.AreEqual(1, matchingLines.Count,
            $"After SetOptionAsync against a no-space pre-existing entry, key '{key}' must appear exactly once. " +
            $"File content:\n{string.Join("\n", lines)}");

        var updatedLine = matchingLines[0].Trim();
        var updatedEqIdx = updatedLine.IndexOf('=');
        var updatedValue = updatedLine[(updatedEqIdx + 1)..].Trim();
        Assert.AreEqual("error", updatedValue,
            $"No-space pre-existing entry must have been replaced with the new value. Actual: '{updatedValue}'.");
    }

    /// <summary>
    /// Regression test for <c>set-editorconfig-option-cross-section-duplicate-key</c>
    /// (gh #735 regression). The exact operator repro: a key already present under a
    /// DIFFERENT C#-applicable section (<c>[*.cs]</c>) must be updated IN PLACE, not
    /// duplicated by an append under the writer's canonical <c>[*.{cs,csx,cake}]</c>
    /// section. The pre-fix writer searched only the canonical section, so a key under
    /// <c>[*.cs]</c> was never found and a second copy was appended — leaving the key
    /// present twice (EditorConfig last-wins kept it functional but the file malformed,
    /// and <c>get_editorconfig_options</c> reported the stale first value).
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_KeyInOtherCSharpSection_UpdatedInPlaceNotDuplicated()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Pre-seed the key under [*.cs] (NOT the writer's canonical [*.{cs,csx,cake}] section).
        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string key = "dotnet_separate_import_directive_groups";
        await File.WriteAllLinesAsync(editorconfigPath,
            ["root = true", "", "[*.cs]", $"{key} = false"], CancellationToken.None);

        // Flip the value. The existing [*.cs] entry must be edited in place.
        var result = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, "true", "set_editorconfig_option", CancellationToken.None);
        Assert.IsFalse(result.CreatedNewFile, "File already existed; CreatedNewFile must be false.");

        var lines = await File.ReadAllLinesAsync(editorconfigPath, CancellationToken.None);
        var matchingLines = lines.Where(l => LineKeyEquals(l, key)).ToList();
        Assert.AreEqual(1, matchingLines.Count,
            $"Key '{key}' present under [*.cs] must be updated in place, not duplicated under " +
            $"[*.{{cs,csx,cake}}]. File content:\n{string.Join("\n", lines)}");

        Assert.AreEqual("true", LineValue(matchingLines[0]),
            "The in-place update must change the value to 'true'.");

        // The reader must now report the single, current value (no stale duplicate shadowing it).
        var options = await EditorConfigService.GetOptionsAsync(
            workspaceId, dogFilePath, CancellationToken.None);
        var entry = options.Options.FirstOrDefault(o =>
            string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry);
        Assert.AreEqual("true", entry!.Value,
            "get_editorconfig_options must surface the updated value, not the pre-update one.");
    }

    /// <summary>
    /// Companion to the cross-section update test: a genuinely-new key (absent from every
    /// C#-applicable section) must be appended exactly once, under the canonical
    /// <c>[*.{cs,csx,cake}]</c> section.
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_NewKey_AppendedExactlyOnce()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Pre-seed a file that has a C#-applicable section but NOT the target key.
        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string existingKey = "indent_size";
        const string newKey = "dotnet_separate_import_directive_groups";
        await File.WriteAllLinesAsync(editorconfigPath,
            ["[*.cs]", $"{existingKey} = 4"], CancellationToken.None);

        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, newKey, "true", "set_editorconfig_option", CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(editorconfigPath, CancellationToken.None);
        Assert.AreEqual(1, lines.Count(l => LineKeyEquals(l, newKey)),
            $"New key '{newKey}' must be appended exactly once. File content:\n{string.Join("\n", lines)}");
        Assert.AreEqual("true", LineValue(lines.First(l => LineKeyEquals(l, newKey))));

        // The pre-existing unrelated key must be untouched (still present once).
        Assert.AreEqual(1, lines.Count(l => LineKeyEquals(l, existingKey)),
            "The append must not disturb the pre-existing unrelated key.");
    }

    private static bool LineKeyEquals(string line, string key)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
            return false;
        var eqIndex = trimmed.IndexOf('=');
        if (eqIndex <= 0) return false;
        return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
    }

    private static string LineValue(string line)
    {
        var trimmed = line.Trim();
        var eqIndex = trimmed.IndexOf('=');
        return trimmed[(eqIndex + 1)..].Trim();
    }

    [TestMethod]
    public void SectionMatchesCSharp_RecognizesCommonGlobs()
    {
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.cs]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.{cs,csx,cake}]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.{vb,cs}]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[**.cs]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.csx]"));

        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.vb]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.{vb,fs}]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.json]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp(""));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("not-a-section"));
    }

    /// <summary>
    /// Coverage for <c>build-test-services-swallowed-exceptions-no-logging</c>:
    /// <see cref="IEditorConfigService.GetOptionsAsync"/> now emits a <see cref="LogLevel.Debug"/>
    /// entry (previously no logger existed at all) when it applies disk-sourced .editorconfig
    /// values over the Roslyn snapshot, naming the .editorconfig path and the supplement/override
    /// counts so a stale-snapshot merge leaves a diagnostic trail.
    /// </summary>
    [TestMethod]
    public async Task GetOptions_DiskSourcedOverrides_LogsDebug()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Write an unloaded analyzer key so the on-disk supplement path (source "disk") fires
        // on the next read — guaranteeing diskSupplementedCount > 0 and thus the Debug log.
        const string unloadedKey = "dotnet_diagnostic.CA9998.severity";
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, unloadedKey, "none", "set_editorconfig_option", CancellationToken.None);

        var logger = new CaptureLogger<EditorConfigService>();
        var service = new EditorConfigService(WorkspaceManager, logger: logger);

        var options = await service.GetOptionsAsync(workspaceId, dogFilePath, CancellationToken.None);
        Assert.IsTrue(
            options.Options.Any(o => string.Equals(o.Key, unloadedKey, StringComparison.OrdinalIgnoreCase)),
            "Precondition: the disk-supplemented key must be present in the returned options.");

        var debug = logger.Entries.SingleOrDefault(e => e.Level == LogLevel.Debug);
        Assert.IsNotNull(debug,
            "Applying disk-sourced .editorconfig values must emit a Debug log. " +
            $"Captured entries: {string.Join("; ", logger.Entries.Select(e => $"{e.Level}:{e.Message}"))}");
        StringAssert.Contains(debug!.Message, options.ApplicableEditorConfigPath!);
    }
}
