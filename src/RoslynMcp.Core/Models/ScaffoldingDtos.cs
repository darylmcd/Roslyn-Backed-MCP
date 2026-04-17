namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a request to scaffold a new type. When <see cref="BaseType"/> or any entry in
/// <see cref="Interfaces"/> resolves to an interface and <see cref="ImplementInterface"/> is
/// <see langword="true"/> (the default), the generated class will include stub
/// implementations (<c>throw new NotImplementedException()</c>) for every interface member.
/// Set <see cref="ImplementInterface"/> to <see langword="false"/> to emit an empty body as
/// before (useful when the caller wants to fill in members manually).
/// </summary>
public sealed record ScaffoldTypeDto(
    string ProjectName,
    string TypeName,
    string TypeKind,
    string? Namespace = null,
    string? BaseType = null,
    IReadOnlyList<string>? Interfaces = null,
    bool ImplementInterface = true);

/// <summary>
/// Represents a request to scaffold tests for multiple target types in a single preview token.
/// All targets share one composite preview which applies atomically via
/// <c>apply_composite_preview</c>.
/// </summary>
public sealed record ScaffoldTestBatchDto(
    string TestProjectName,
    IReadOnlyList<ScaffoldTestBatchTargetDto> Targets,
    string TestFramework = "auto");

/// <summary>
/// Single target within a <see cref="ScaffoldTestBatchDto"/>.
/// </summary>
public sealed record ScaffoldTestBatchTargetDto(
    string TargetTypeName,
    string? TargetMethodName = null);

/// <summary>
/// Represents a request to scaffold tests for a target type or method.
/// </summary>
/// <param name="TestProjectName">Name or absolute path of the target test project.</param>
/// <param name="TargetTypeName">Name of the production type under test.</param>
/// <param name="TargetMethodName">Optional: focus the generated stub on a single method.</param>
/// <param name="TestFramework">Test framework: <c>mstest</c>, <c>xunit</c>, <c>nunit</c>, or <c>auto</c>.</param>
/// <param name="ReferenceTestFile">
/// Optional absolute path to an existing sibling test file whose scaffolding should be
/// replicated — class attributes (e.g. <c>[TestClass]</c>, <c>[Trait(…)]</c>), base class,
/// and constructor-injected fixture types (e.g. <c>IClassFixture&lt;CustomWebApplicationFactory&gt;</c>).
/// When omitted, the scaffolder auto-detects the most-recently-modified <c>*Tests.cs</c> file
/// in the target test project and uses that as the reference. Set to the empty string to
/// opt out of inference entirely.
/// </param>
public sealed record ScaffoldTestDto(
    string TestProjectName,
    string TargetTypeName,
    string? TargetMethodName = null,
    string TestFramework = "auto",
    string? ReferenceTestFile = null);

/// <summary>
/// Represents a request to remove dead code symbols.
/// </summary>
public sealed record DeadCodeRemovalDto(
    IReadOnlyList<string> SymbolHandles,
    bool RemoveEmptyFiles = false);
