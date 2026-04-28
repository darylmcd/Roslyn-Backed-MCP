using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Regression coverage for `symbol-info-not-found-message-locator-vs-location` (P4 — UX).
/// Pre-fix, every <c>symbol_info</c> not-found error returned the verbatim string
/// <c>"No symbol found at the specified location"</c> regardless of which locator field the
/// caller actually supplied — including the cases where no source location was provided at all.
/// Post-fix the message names the locator field (filePath:line:col, symbolHandle, or
/// metadataName) so callers can correlate the failure with the input they sent.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolInfoNotFoundMessageTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// metadataName-only locator: the most common motivator for the bug — agents resolved a
    /// fully-qualified type name (from DI registrations, document_symbols, etc.) and got told
    /// the symbol was missing "at the specified location" they never supplied. Post-fix the
    /// error must echo the metadata name so the agent knows what input failed to resolve.
    /// </summary>
    [TestMethod]
    public async Task SymbolInfo_MetadataNameOnly_NotFound_NamesTheMetadataName()
    {
        const string bogusName = "SampleLib.DefinitelyDoesNotExist__Bogus";

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await SymbolTools.GetSymbolInfo(
                WorkspaceExecutionGate,
                SymbolSearchService,
                WorkspaceId,
                metadataName: bogusName,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "metadata name",
            $"Error must name the locator field that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, bogusName,
            $"Error must echo the offending metadata name so the caller can correlate. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("at the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"at the specified location\" wording for metadataName-only callers. Got: {ex.Message}");
    }

    /// <summary>
    /// Source-location locator: the case where the legacy wording was at least directionally
    /// correct, but still vague — "the specified location" leaves the caller guessing whether
    /// they passed the right file. Post-fix the error must include the file:line:col literal
    /// so the caller can verify their input round-tripped correctly.
    /// </summary>
    [TestMethod]
    public async Task SymbolInfo_SourceLocationOnly_NotFound_NamesTheFileLineCol()
    {
        // Build an absolute path that's definitely not in the loaded workspace so the resolver
        // returns null (no symbol). The exact path text is what we want to assert on, so the
        // value is deterministic across machines.
        var bogusFile = Path.Combine(Path.GetTempPath(), "definitely_not_in_workspace.cs");

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await SymbolTools.GetSymbolInfo(
                WorkspaceExecutionGate,
                SymbolSearchService,
                WorkspaceId,
                filePath: bogusFile,
                line: 1,
                column: 1,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, bogusFile,
            $"Error must echo the file path that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, "1:1",
            $"Error must echo the line:column position that was supplied. Got: {ex.Message}");
        // The new message format is `"No symbol found at <file>:<line>:<col>"` — the literal
        // `"the specified location"` phrase should be gone. Assert the substring is absent so a
        // future caller who reverts the fix to a generic message gets caught here.
        Assert.IsFalse(
            ex.Message.Contains("the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"the specified location\" wording. Got: {ex.Message}");
    }

    /// <summary>
    /// symbolHandle-only locator: the third leg of the locator triad. A handle that decodes
    /// correctly (well-formed base64+JSON payload) but points at a symbol that doesn't exist
    /// in the loaded workspace exercises the resolver's "valid handle, symbol not found" path.
    /// Malformed handles (non-base64, non-JSON, etc.) raise <see cref="ArgumentException"/>
    /// upstream and never reach the not-found branch — that's a separate code path tested
    /// elsewhere. Post-fix the not-found message must name the handle the caller supplied.
    /// </summary>
    [TestMethod]
    public async Task SymbolInfo_SymbolHandleOnly_NotFound_NamesTheSymbolHandle()
    {
        // Construct a structurally-valid handle whose payload references a metadata name that
        // doesn't exist in SampleSolution — the deserializer accepts the payload, the resolver
        // looks for `SampleLib.NotARealType`, fails, and returns null. Result: the caller
        // reaches the SymbolTools.cs throw site we're testing.
        var validButMissingHandle = EncodeHandle("SampleLib.DefinitelyMissingType_Bogus");

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await SymbolTools.GetSymbolInfo(
                WorkspaceExecutionGate,
                SymbolSearchService,
                WorkspaceId,
                symbolHandle: validButMissingHandle,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "symbol handle",
            $"Error must name the locator field that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, validButMissingHandle,
            $"Error must echo the offending symbol handle so the caller can correlate. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("at the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"at the specified location\" wording for handle-only callers. Got: {ex.Message}");
    }

    /// <summary>
    /// Encodes a synthetic symbol handle whose only payload field is <c>MetadataName</c>. Matches
    /// the on-the-wire shape of <c>SymbolHandleSerializer.SymbolHandlePayload</c> exactly enough
    /// for <c>ParseHandlePayload</c> to accept it; the resolver then fails to find the metadata
    /// name and returns null. Used to exercise the "valid handle, symbol not found" branch
    /// without taking a dependency on the internal payload type.
    /// </summary>
    private static string EncodeHandle(string metadataName)
    {
        var json = $"{{\"MetadataName\":\"{metadataName}\"}}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Long pasted metadata names — e.g. the full <c>ToDisplayString()</c> of a constructed
    /// generic type — should be truncated in the error message so the envelope doesn't leak
    /// kilobytes of caller input. Risk called out in the planning row's Risks field.
    /// </summary>
    [TestMethod]
    public async Task SymbolInfo_LongMetadataName_NotFound_TruncatesEchoedValue()
    {
        // 2 KB of locator text — far past any reasonable metadata-name length. The new helper
        // truncates to 200 chars + ellipsis, so the response must NOT contain the tail of the
        // input but MUST contain a leading prefix and the truncation marker.
        var hugeName = "SampleLib.Bogus_" + new string('X', 2000);

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await SymbolTools.GetSymbolInfo(
                WorkspaceExecutionGate,
                SymbolSearchService,
                WorkspaceId,
                metadataName: hugeName,
                ct: CancellationToken.None));

        Assert.IsTrue(ex.Message.Length < hugeName.Length,
            $"Error message must be shorter than the offending input (truncation enforced). Got len={ex.Message.Length}, input len={hugeName.Length}.");
        StringAssert.Contains(ex.Message, "SampleLib.Bogus_",
            $"Error must still echo the leading prefix so the caller can correlate. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, "...",
            $"Error must surface the truncation marker so the caller knows the value was clipped. Got: {ex.Message}");
    }
}
