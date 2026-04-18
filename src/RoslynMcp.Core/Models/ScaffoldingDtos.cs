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
/// Represents a request to scaffold the FIRST test file for a service that has no existing
/// fixture in the test project. Distinct from <see cref="ScaffoldTestDto"/> in that:
/// <list type="bullet">
///   <item>The service is identified by metadata name (<c>Namespace.TypeName</c>) — not bare type name.</item>
///   <item>The emitted fixture covers ALL public methods on the service (one smoke test each), not a single method.</item>
///   <item>Boilerplate shape is derived from up to three most-recently-modified sibling fixtures so ClassInitialize / setup conventions carry over.</item>
///   <item>The output file is named <c>&lt;Service&gt;Tests.cs</c> (matches the sibling-fixture convention) — NOT <c>&lt;Service&gt;GeneratedTests.cs</c>.</item>
///   <item>The call fails when the destination file already exists (callers should use <see cref="ScaffoldTestDto"/> with <c>scaffold_test_preview</c> to add tests to an existing fixture).</item>
/// </list>
/// </summary>
/// <param name="ServiceMetadataName">Fully-qualified type name of the production service (e.g. <c>RoslynMcp.Roslyn.Services.RestructureService</c>).</param>
/// <param name="TestProjectName">Optional name or absolute path of the destination test project. When omitted, the scaffolder infers the project that references the service's containing project AND whose name ends in <c>.Tests</c>.</param>
/// <param name="TestFramework">Test framework: <c>mstest</c>, <c>xunit</c>, <c>nunit</c>, or <c>auto</c>.</param>
public sealed record ScaffoldFirstTestFileDto(
    string ServiceMetadataName,
    string? TestProjectName = null,
    string TestFramework = "auto");

/// <summary>
/// Represents a request to remove dead code symbols.
/// </summary>
public sealed record DeadCodeRemovalDto(
    IReadOnlyList<string> SymbolHandles,
    bool RemoveEmptyFiles = false);
