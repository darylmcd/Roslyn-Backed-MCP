namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Internal signal that a preview's reviewed change cannot be replayed onto the current workspace
/// solution (its target document or project is gone, or it carries no replayable identity), so
/// applying it would silently drop the edit. <c>RefactoringService.ApplyRefactoringAsync</c>
/// converts it to the existing invalid-preview refusal in the response DTO; it never reaches the host.
/// </summary>
internal sealed class PreviewRebaseConflictException(string reason) : Exception(reason);
