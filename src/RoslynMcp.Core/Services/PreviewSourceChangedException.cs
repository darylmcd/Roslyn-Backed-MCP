namespace RoslynMcp.Core.Services;

/// <summary>
/// A composite preview token was refused because a target file changed on disk after the preview
/// was stored (ADR 0033). It is a <see cref="PreviewTokenStaleException"/>, so the envelope category
/// stays <c>PreviewTokenStale</c> and callers recover the same way (re-issue the paired
/// <c>*_preview</c> call), but its cause is not a workspace reload: the error handler reports
/// <see cref="PublicMessage"/> instead of the fixed reload text.
/// </summary>
/// <remarks>
/// The refusal happens before the token is consumed and before any write, so restoring the file also
/// makes the same token redeemable again. <see cref="PublicMessage"/> names the file by name only:
/// never its absolute path and never the token, which public envelopes omit.
/// </remarks>
public sealed class PreviewSourceChangedException : PreviewTokenStaleException
{
    public PreviewSourceChangedException(string previewToken, string changedFileName)
        : base(previewToken, BuildMessage(changedFileName))
    {
        ChangedFileName = changedFileName;
    }

    /// <summary>The file name (no directory) of the first target that no longer matches the preview.</summary>
    public string ChangedFileName { get; }

    public string PublicMessage => Message;

    private static string BuildMessage(string changedFileName) =>
        $"The composite preview was not applied: '{changedFileName}' changed on disk after the preview was created, " +
        "so applying it would overwrite that change. The token was not consumed. " +
        "Restore the file to its previewed state, or re-issue the paired *_preview call.";
}
