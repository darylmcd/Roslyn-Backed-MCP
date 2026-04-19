using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 7 implementation. Walks the supplied operation list sequentially. Each operation
/// produces a delta against the accumulating Solution; the final snapshot is stored once via
/// <see cref="IPreviewStore"/> so a single token covers the whole batch.
///
/// <para>
/// Operations are atomic at preview time: the first op that fails aborts the entire preview
/// (no partial token is issued). Order matters — later ops see the rewritten text from earlier
/// ops, which is documented in the operation kind table on <see cref="ISymbolRefactorService"/>.
/// </para>
/// </summary>
public sealed class SymbolRefactorService : ISymbolRefactorService
{
    private const int MaxOperations = 25;
    private const int MaxFilesAffected = 500;

    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly IRefactoringService _refactoringService;
    private readonly IEditService _editService;
    private readonly IRestructureService _restructureService;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly IDiRegistrationService _diRegistrationService;

    public SymbolRefactorService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        IRefactoringService refactoringService,
        IEditService editService,
        IRestructureService restructureService,
        ICompositePreviewStore compositePreviewStore,
        IDiRegistrationService diRegistrationService)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _refactoringService = refactoringService;
        _editService = editService;
        _restructureService = restructureService;
        _compositePreviewStore = compositePreviewStore;
        _diRegistrationService = diRegistrationService;
    }

    public async Task<RefactoringPreviewDto> PreviewAsync(
        string workspaceId, IReadOnlyList<SymbolRefactorOperation> operations, CancellationToken ct)
    {
        if (operations is null || operations.Count == 0)
            throw new InvalidOperationException("symbol_refactor_preview requires at least one operation.");
        if (operations.Count > MaxOperations)
            throw new InvalidOperationException(
                $"symbol_refactor_preview accepts at most {MaxOperations} operations per call.");

        var aggregatedDiffs = new Dictionary<string, FileChangeDto>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new List<string>();

        for (var i = 0; i < operations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var op = operations[i];
            try
            {
                var stepPreview = await ExecuteOperationAsync(workspaceId, op, ct).ConfigureAwait(false);
                descriptions.Add($"[{i + 1}/{operations.Count}] {op.Kind}: {stepPreview.Description}");

                foreach (var change in stepPreview.Changes)
                {
                    aggregatedDiffs[change.FilePath] = change;
                }

                // The previous step persisted its own preview token; we don't need it after the
                // text changes have flowed into the workspace. Apply the step so the next op
                // sees the rewritten state.
                await _refactoringService.ApplyRefactoringAsync(stepPreview.PreviewToken, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"symbol_refactor_preview operation #{i + 1} ({op.Kind}) failed: {ex.Message}", ex);
            }

            if (aggregatedDiffs.Count > MaxFilesAffected)
            {
                throw new InvalidOperationException(
                    $"symbol_refactor_preview exceeded the {MaxFilesAffected}-file diff cap. Split the operation list.");
            }
        }

        // After applying every step the workspace now reflects the final state. Snapshot the
        // current solution and store it as the composite preview token. (Note: each sub-op
        // already wrote to disk via apply — symbol_refactor_apply will be a no-op; this hands
        // the user a unified diff while leaving the workspace in the post-refactor state.)
        var finalSolution = _workspace.GetCurrentSolution(workspaceId);
        var description = "Composite refactor:\n  " + string.Join("\n  ", descriptions);
        var token = _previewStore.Store(workspaceId, finalSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(
            PreviewToken: token,
            Description: description,
            Changes: aggregatedDiffs.Values.ToArray(),
            Warnings: null);
    }

    private async Task<RefactoringPreviewDto> ExecuteOperationAsync(
        string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        return op.Kind?.ToLowerInvariant() switch
        {
            "rename" => await ExecuteRenameAsync(workspaceId, op, ct).ConfigureAwait(false),
            "edit" => await ExecuteEditAsync(workspaceId, op, ct).ConfigureAwait(false),
            "restructure" => await ExecuteRestructureAsync(workspaceId, op, ct).ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"Unsupported operation kind '{op.Kind}'. Valid: rename, edit, restructure."),
        };
    }

    private Task<RefactoringPreviewDto> ExecuteRenameAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(op.NewName))
            throw new ArgumentException("kind='rename' requires NewName.");
        var locator = new SymbolLocator(op.FilePath, op.Line, op.Column, op.SymbolHandle, op.MetadataName);
        locator.Validate();
        return _refactoringService.PreviewRenameAsync(workspaceId, locator, op.NewName, ct);
    }

    private Task<RefactoringPreviewDto> ExecuteEditAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (op.FileEdits is null || op.FileEdits.Count == 0)
            throw new ArgumentException("kind='edit' requires FileEdits.");
        return _editService.PreviewMultiFileTextEditsAsync(workspaceId, op.FileEdits, ct, skipSyntaxCheck: false);
    }

    private Task<RefactoringPreviewDto> ExecuteRestructureAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(op.Pattern) || op.Goal is null)
            throw new ArgumentException("kind='restructure' requires Pattern and Goal.");
        return _restructureService.PreviewRestructureAsync(
            workspaceId, op.Pattern, op.Goal,
            new RestructureScope(op.ScopeFilePath, op.ScopeProjectName), ct);
    }

    /// <summary>
    /// <c>composite-split-service-di-registration</c>: splits <paramref name="sourceType"/>
    /// into N partition implementations plus a forwarding facade and emits DI-registration
    /// deltas for the host's <c>Add*</c> lines in one composite preview. Lifetime
    /// (<c>Transient</c>/<c>Scoped</c>/<c>Singleton</c>) is inferred from the existing
    /// registration; when the registration is missing the preview still emits the partition
    /// and facade mutations and attaches a warning rather than failing.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewSplitServiceWithDiAsync(
        string workspaceId,
        string sourceFilePath,
        string sourceType,
        IReadOnlyList<SplitServicePartition> partitions,
        string? hostRegistrationFile,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentException("sourceFilePath is required.", nameof(sourceFilePath));
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("sourceType is required.", nameof(sourceType));
        if (partitions is null || partitions.Count == 0)
            throw new ArgumentException("At least one partition is required.", nameof(partitions));
        if (partitions.Any(partition => string.IsNullOrWhiteSpace(partition.TypeName) || partition.MemberNames is null || partition.MemberNames.Count == 0))
            throw new ArgumentException("Each partition requires TypeName and at least one MemberName.", nameof(partitions));

        var duplicatedMembers = partitions
            .SelectMany(partition => partition.MemberNames)
            .GroupBy(memberName => memberName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicatedMembers.Length > 0)
        {
            throw new ArgumentException(
                $"Each member must appear in exactly one partition. Duplicated: {string.Join(", ", duplicatedMembers)}",
                nameof(partitions));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, sourceFilePath)
            ?? throw new InvalidOperationException($"Document not found: {sourceFilePath}");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, sourceType, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{sourceType}' was not found in '{sourceFilePath}'.");

        // Validate every requested member exists and is a method (facade forwarding supports
        // methods cleanly; we keep the first version intentionally method-only to avoid writing
        // a richer generator for properties / fields in scope).
        var methodDeclarations = typeDeclaration.Members.OfType<MethodDeclarationSyntax>().ToArray();
        var memberToPartition = new Dictionary<string, SplitServicePartition>(StringComparer.Ordinal);
        foreach (var partition in partitions)
        {
            foreach (var memberName in partition.MemberNames)
            {
                memberToPartition[memberName] = partition;
            }
        }

        var missingMembers = memberToPartition.Keys
            .Where(name => !methodDeclarations.Any(method => string.Equals(method.Identifier.ValueText, name, StringComparison.Ordinal)))
            .ToArray();
        if (missingMembers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Method(s) not found on '{sourceType}': {string.Join(", ", missingMembers)}");
        }

        var namespaceName = GetNamespaceName(typeDeclaration);
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException($"Source file '{sourceFilePath}' must have a parent directory.");
        var usings = StripLeadingTriviaFromFirstUsing(root.Usings);
        var warnings = new List<string>();
        var mutations = new List<CompositeFileMutation>();
        var changes = new List<FileChangeDto>();

        // 1) Create partition files.
        foreach (var partition in partitions)
        {
            var partitionMethods = methodDeclarations
                .Where(method => partition.MemberNames.Contains(method.Identifier.ValueText, StringComparer.Ordinal))
                .Select(method => NormalizeMemberForPartition(method))
                .ToArray();

            var partitionFilePath = Path.Combine(sourceDirectory, $"{partition.TypeName}.cs");
            var partitionContent = BuildPartitionFile(namespaceName, partition.TypeName, partitionMethods, usings);
            mutations.Add(new CompositeFileMutation(partitionFilePath, partitionContent));
            changes.Add(new FileChangeDto(partitionFilePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, partitionContent, partitionFilePath)));
        }

        // 2) Rewrite the original file: the source type becomes a forwarding facade whose
        //    members delegate to partition implementations held in private fields. Every
        //    partition is injected via the facade's primary-style constructor.
        var originalContent = await File.ReadAllTextAsync(sourceFilePath, ct).ConfigureAwait(false);
        var facadeContent = BuildFacadeFile(namespaceName, sourceType, typeDeclaration, methodDeclarations, memberToPartition, partitions, usings);
        mutations.Add(new CompositeFileMutation(sourceFilePath, facadeContent));
        changes.Add(new FileChangeDto(sourceFilePath, DiffGenerator.GenerateUnifiedDiff(originalContent, facadeContent, sourceFilePath)));

        // 3) Rewrite the host DI registration file if provided. When hostRegistrationFile is
        //    null we fall back to scanning the workspace for any registration of sourceType.
        var registrationInfo = await TryResolveRegistrationAsync(workspaceId, sourceType, hostRegistrationFile, ct).ConfigureAwait(false);
        if (registrationInfo is null)
        {
            warnings.Add(
                hostRegistrationFile is null
                    ? $"No DI registration for '{sourceType}' was discovered in the workspace; the preview omits DI-registration deltas."
                    : $"No DI registration for '{sourceType}' was found in '{hostRegistrationFile}'; the preview omits DI-registration deltas.");
        }
        else
        {
            var (registrationFilePath, lifetime) = registrationInfo.Value;
            var registrationContent = await File.ReadAllTextAsync(registrationFilePath, ct).ConfigureAwait(false);
            var rewrittenRegistration = RewriteDiRegistrations(registrationContent, sourceType, lifetime, partitions);
            if (string.Equals(registrationContent, rewrittenRegistration, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"A '{sourceType}' DI registration was discovered in '{registrationFilePath}' but no textual rewrite matched. Inspect the file manually.");
            }
            else
            {
                mutations.Add(new CompositeFileMutation(registrationFilePath, rewrittenRegistration));
                changes.Add(new FileChangeDto(registrationFilePath, DiffGenerator.GenerateUnifiedDiff(registrationContent, rewrittenRegistration, registrationFilePath)));
            }
        }

        var description = $"Split service '{sourceType}' into {partitions.Count} partition(s) with forwarding facade"
            + (registrationInfo is not null ? " and DI-registration rewrite" : "");
        var token = _compositePreviewStore.Store(workspaceId, _workspace.GetCurrentVersion(workspaceId), description, mutations);

        return new RefactoringPreviewDto(
            PreviewToken: token,
            Description: description,
            Changes: changes,
            Warnings: warnings.Count == 0 ? null : warnings);
    }

    private async Task<(string FilePath, string Lifetime)?> TryResolveRegistrationAsync(
        string workspaceId, string sourceType, string? hostRegistrationFile, CancellationToken ct)
    {
        // Primary lookup: semantic scan via DiRegistrationService. Requires the workspace to
        // reference Microsoft.Extensions.DependencyInjection so that symbols resolve.
        var registrations = await _diRegistrationService.GetDiRegistrationsAsync(workspaceId, null, ct).ConfigureAwait(false);
        foreach (var registration in registrations)
        {
            if (!IsServiceTypeMatch(registration, sourceType))
            {
                continue;
            }
            if (hostRegistrationFile is not null &&
                !string.Equals(Path.GetFullPath(registration.FilePath), Path.GetFullPath(hostRegistrationFile), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (registration.FilePath, NormalizeLifetime(registration.Lifetime));
        }

        // Fallback: textual scan. Skipping the semantic match (because the source doesn't
        // reference `Microsoft.Extensions.DependencyInjection`, or the symbol otherwise fails
        // to resolve) shouldn't blind us to a registration that exists literally as
        // `services.AddTransient<Foo>()` in the file.
        var candidateFiles = new List<string>();
        if (hostRegistrationFile is not null)
        {
            candidateFiles.Add(hostRegistrationFile);
        }
        else
        {
            var solution = _workspace.GetCurrentSolution(workspaceId);
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (!string.IsNullOrWhiteSpace(document.FilePath) &&
                        string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        candidateFiles.Add(document.FilePath!);
                    }
                }
            }
        }

        var textualPattern = new Regex(
            @"services\s*\.\s*Add(?<lifetime>Transient|Scoped|Singleton)\s*<\s*(?<generics>[^>]*?)\s*>\s*\(",
            RegexOptions.CultureInvariant);

        foreach (var candidate in candidateFiles)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(candidate, ct).ConfigureAwait(false);
            foreach (Match match in textualPattern.Matches(content))
            {
                if (GenericsReferenceSourceType(match.Groups["generics"].Value, sourceType))
                {
                    return (candidate, NormalizeLifetime(match.Groups["lifetime"].Value));
                }
            }
        }

        return null;
    }

    private static bool IsServiceTypeMatch(DiRegistrationDto registration, string sourceType)
    {
        return ShortTypeName(registration.ImplementationType).Equals(sourceType, StringComparison.Ordinal) ||
               ShortTypeName(registration.ServiceType).Equals(sourceType, StringComparison.Ordinal);
    }

    private static string NormalizeLifetime(string lifetime)
    {
        // DiRegistrationService reports lifetimes as words ("Transient" / "Scoped" / "Singleton")
        // or as verbatim method suffixes ("AddTransient" / "AddScoped" / "AddSingleton"). Map
        // both shapes to the canonical method stem so callers can build AddX<IFoo, Foo>().
        var trimmed = lifetime?.Trim() ?? "Transient";
        if (trimmed.StartsWith("Add", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[3..];
        }
        return trimmed switch
        {
            var value when value.Equals("Singleton", StringComparison.OrdinalIgnoreCase) => "Singleton",
            var value when value.Equals("Scoped", StringComparison.OrdinalIgnoreCase) => "Scoped",
            _ => "Transient"
        };
    }

    private static string ShortTypeName(string typeName)
    {
        var tickIndex = typeName.IndexOf('<');
        var trimmed = tickIndex >= 0 ? typeName[..tickIndex] : typeName;
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }

    private static string RewriteDiRegistrations(
        string content,
        string sourceType,
        string lifetime,
        IReadOnlyList<SplitServicePartition> partitions)
    {
        // Build the replacement block: one Add<Lifetime> call per partition, plus the facade
        // (sourceType preserved so existing consumers resolving `sourceType` keep working).
        // The facade registration retains the original generic/concrete shape of the source
        // registration.
        var replacement = new StringBuilder();
        foreach (var partition in partitions)
        {
            replacement.Append("services.Add").Append(lifetime)
                       .Append('<').Append(partition.TypeName).Append(">();\n");
        }
        replacement.Append("services.Add").Append(lifetime)
                   .Append('<').Append(sourceType).Append(">();");

        var replacementText = replacement.ToString();

        // Match the original registration in several common shapes:
        //   services.AddTransient<IFoo, Foo>();
        //   services.AddTransient<Foo>();
        //   services.AddTransient<IFoo>(sp => new Foo(sp));
        // Each match is rewritten to the partition+facade block, preserving the original
        // statement's indentation so the diff stays local.
        var pattern = new Regex(
            @"^(?<indent>[ \t]*)services\s*\.\s*Add(?<lifetime>Transient|Scoped|Singleton)\s*<\s*(?<generics>[^>]*?)\s*>\s*\([^;]*\)\s*;",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var rewritten = pattern.Replace(content, match =>
        {
            var generics = match.Groups["generics"].Value;
            if (!GenericsReferenceSourceType(generics, sourceType))
            {
                return match.Value;
            }

            var indent = match.Groups["indent"].Value;
            return IndentBlock(replacementText, indent);
        });

        return rewritten;
    }

    private static bool GenericsReferenceSourceType(string generics, string sourceType)
    {
        foreach (var part in generics.Split(','))
        {
            if (ShortTypeName(part.Trim()).Equals(sourceType, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string IndentBlock(string block, string indent)
    {
        var builder = new StringBuilder();
        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }
            var line = lines[i];
            if (line.Length > 0)
            {
                builder.Append(indent).Append(line);
            }
        }
        return builder.ToString();
    }

    private static MethodDeclarationSyntax NormalizeMemberForPartition(MethodDeclarationSyntax method)
    {
        // Strip attribute lists and normalize leading trivia so the moved method sits cleanly
        // inside the generated partition file. Attributes on the source declaration are kept on
        // the facade's forwarding stub if the user needs them; the authoritative implementation
        // on the partition gets a neutral decoration.
        return method
            .WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>())
            .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed));
    }

    private static string BuildPartitionFile(
        string namespaceName,
        string partitionTypeName,
        IReadOnlyList<MethodDeclarationSyntax> methods,
        SyntaxList<UsingDirectiveSyntax> usings)
    {
        var classDecl = SyntaxFactory.ClassDeclaration(partitionTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(methods));

        var compilationUnit = SyntaxFactory.CompilationUnit().WithUsings(usings);
        compilationUnit = WrapInNamespace(compilationUnit, namespaceName, classDecl);
        return compilationUnit.NormalizeWhitespace().ToFullString() + Environment.NewLine;
    }

    private static string BuildFacadeFile(
        string namespaceName,
        string sourceType,
        TypeDeclarationSyntax originalDeclaration,
        IReadOnlyList<MethodDeclarationSyntax> allMethods,
        IReadOnlyDictionary<string, SplitServicePartition> memberToPartition,
        IReadOnlyList<SplitServicePartition> partitions,
        SyntaxList<UsingDirectiveSyntax> usings)
    {
        // Private readonly field per partition, injected via constructor.
        var fieldMembers = partitions
            .Select(partition => SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(partition.TypeName))
                    .AddVariables(SyntaxFactory.VariableDeclarator($"_{LowerFirst(partition.TypeName)}")))
                .AddModifiers(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)))
            .ToArray();

        // Constructor taking one parameter per partition.
        var ctorParameters = partitions
            .Select(partition => SyntaxFactory.Parameter(SyntaxFactory.Identifier(LowerFirst(partition.TypeName)))
                .WithType(SyntaxFactory.ParseTypeName(partition.TypeName)))
            .ToArray();
        var ctorBody = SyntaxFactory.Block(partitions.Select(partition =>
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName($"_{LowerFirst(partition.TypeName)}"),
                    SyntaxFactory.IdentifierName(LowerFirst(partition.TypeName))))));
        var constructor = SyntaxFactory.ConstructorDeclaration(sourceType)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(ctorParameters)
            .WithBody(ctorBody);

        // Forwarding members — one method per original method delegating to the corresponding
        // partition field. Methods that did not make it into any partition are copied through
        // verbatim so the facade's surface matches the original type.
        var forwardingMembers = allMethods.Select(method =>
        {
            if (memberToPartition.TryGetValue(method.Identifier.ValueText, out var partition))
            {
                return BuildForwardingMethod(method, partition.TypeName);
            }

            // Preserve unpartitioned methods on the facade as-is (minus attributes) — they
            // continue to contain their original logic.
            return method
                .WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>())
                .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed));
        });

        var members = new List<MemberDeclarationSyntax>();
        members.AddRange(fieldMembers);
        members.Add(constructor);
        members.AddRange(forwardingMembers);

        var classDecl = SyntaxFactory.ClassDeclaration(sourceType)
            .WithModifiers(originalDeclaration.Modifiers)
            .WithMembers(SyntaxFactory.List(members));

        var compilationUnit = SyntaxFactory.CompilationUnit().WithUsings(usings);
        compilationUnit = WrapInNamespace(compilationUnit, namespaceName, classDecl);
        return compilationUnit.NormalizeWhitespace().ToFullString() + Environment.NewLine;
    }

    private static MethodDeclarationSyntax BuildForwardingMethod(MethodDeclarationSyntax original, string partitionTypeName)
    {
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName($"_{LowerFirst(partitionTypeName)}"),
                SyntaxFactory.IdentifierName(original.Identifier.ValueText)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                original.ParameterList.Parameters.Select(parameter =>
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Identifier))))));

        var body = IsVoidReturn(original.ReturnType)
            ? SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(invocation))
            : SyntaxFactory.Block(SyntaxFactory.ReturnStatement(invocation));

        return SyntaxFactory.MethodDeclaration(original.ReturnType, original.Identifier)
            .WithModifiers(original.Modifiers)
            .WithTypeParameterList(original.TypeParameterList)
            .WithParameterList(original.ParameterList)
            .WithBody(body);
    }

    private static bool IsVoidReturn(TypeSyntax returnType)
    {
        return returnType is PredefinedTypeSyntax predefined &&
               predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
    }

    private static CompilationUnitSyntax WrapInNamespace(
        CompilationUnitSyntax compilationUnit,
        string namespaceName,
        MemberDeclarationSyntax member)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return compilationUnit.WithMembers(SyntaxFactory.SingletonList(member));
        }

        var nsDecl = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
            .WithNamespaceKeyword(
                SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithMembers(SyntaxFactory.SingletonList(member));
        return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(nsDecl));
    }

    private static SyntaxList<UsingDirectiveSyntax> StripLeadingTriviaFromFirstUsing(SyntaxList<UsingDirectiveSyntax> usings)
    {
        if (usings.Count == 0)
        {
            return usings;
        }

        var first = usings[0];
        var stripped = first.WithLeadingTrivia(SyntaxFactory.TriviaList());
        return usings.Replace(first, stripped);
    }

    private static string GetNamespaceName(TypeDeclarationSyntax declaration)
    {
        return declaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name.ToString() ?? string.Empty;
    }

    private static string LowerFirst(string value)
    {
        return string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    // record-field-add-satellite-member-sync: synthesizes satellite-member edits when a
    // record/class gains a new field. Strategy:
    //   1) Resolve the target type and enumerate its existing fields/properties.
    //   2) For each existing field, scan the target type's declaring document (and the
    //      sibling types declared alongside it) for satellite-site kinds — a sibling
    //      mirror type (Snapshot/Dto/Builder) property with the same name, a Clone/With
    //      method that assigns the field, an Increment{Field} method, a ToJson-style
    //      switch case literal, etc.
    //   3) Build per-field coverage sets. Require ≥2 sibling fields with identical
    //      coverage before declaring the set as "the pattern" — keeps us conservative.
    //   4) For each pattern kind, synthesize one edit for the new field at a known
    //      insertion anchor (after the last matching line for that kind).
    //   5) Wrap the edits into a composite-preview token so callers can apply via the
    //      same apply_composite_preview tool used by sibling SymbolRefactor* previews.
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satellite-site kinds the detector recognizes. Each kind corresponds to a structural
    /// pattern the user wants kept in sync when a new field is added. Names match the
    /// documented <c>InferredPattern</c> strings on <see cref="RecordFieldAddSatelliteDto"/>.
    /// </summary>
    private static class SatelliteKind
    {
        public const string SnapshotTypeField = "SnapshotType.Field";
        public const string CloneMethodBody = "CloneMethodBody";
        public const string WithMethodAssignment = "WithMethod.Assignment";
        public const string IncrementMethod = "IncrementMethod";
        public const string ToJsonCase = "ToJson.Case";
    }

    /// <inheritdoc />
    public async Task<RecordFieldAddSatelliteDto> PreviewRecordFieldAddWithSatellitesAsync(
        string workspaceId,
        string typeMetadataName,
        string newFieldName,
        string newFieldType,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeMetadataName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFieldType);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveByMetadataNameAsync(solution, typeMetadataName, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            throw new KeyNotFoundException($"No type resolved for metadata name '{typeMetadataName}'.");
        }

        var newField = new NewSatelliteFieldDto(newFieldName, newFieldType);

        // Pull the primary declaration file. Satellite patterns live alongside the target
        // type (same file or same project) — we do not go cross-solution because that would
        // produce noise and slow the inference. The plan example from b-155/b-156 is a
        // single-file pattern: a struct with a counter method + a mirror snapshot struct
        // together in one file.
        var declaringSyntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaringSyntaxRef is null)
        {
            return EmptyResult(typeSymbol, newField,
                "Target type has no source declaration (metadata-only).",
                "No satellite edits — target type is metadata-only.");
        }

        var syntax = await declaringSyntaxRef.GetSyntaxAsync(ct).ConfigureAwait(false);
        var declaringRoot = await syntax.SyntaxTree.GetRootAsync(ct).ConfigureAwait(false);
        var declaringTree = syntax.SyntaxTree;
        var declaringDoc = solution.GetDocument(declaringTree)
            ?? throw new InvalidOperationException($"Could not map declaring syntax tree to a Document for '{typeMetadataName}'.");

        // Existing fields = every instance field/property on the target type that has a
        // source declaration. We use the names to locate satellite sites — metadata-only
        // members cannot participate in source-text pattern-matching.
        var existingFields = ExtractExistingFieldNames(typeSymbol);
        if (existingFields.Count == 0)
        {
            return EmptyResult(typeSymbol, newField,
                "Target type has no existing fields/properties — no sibling pattern to infer from.",
                "No satellite edits — target type has no existing fields.");
        }

        // The pattern must be inferred from ≥2 sibling fields. A single-field type can't
        // establish "the pattern" because there is nothing to compare against.
        if (existingFields.Count < 2)
        {
            return EmptyResult(typeSymbol, newField,
                $"Only 1 existing field ('{existingFields[0]}') — pattern requires ≥2 sibling fields with identical satellite coverage.",
                $"No satellite edits — only 1 existing field ('{existingFields[0]}').");
        }

        // Scan the declaring document for sibling types + satellite method bodies. We
        // include the whole compilation-unit root because mirror types (Snapshot / Dto /
        // Builder) frequently live next to the target type in the same file, and the
        // plan's example explicitly mirrors this: `ParseCounters` + `ParseMetricsSnapshot`
        // in one file.
        var sourceText = await declaringTree.GetTextAsync(ct).ConfigureAwait(false);
        var analysis = SatelliteAnalysis.Analyze(declaringRoot, typeSymbol, existingFields, sourceText);

        if (analysis.InferredPattern.Count == 0)
        {
            return EmptyResult(typeSymbol, newField,
                analysis.DetectionReason,
                $"No satellite edits — {analysis.DetectionReason.ToLowerInvariant()}");
        }

        // Synthesize one edit per pattern kind.
        var edits = new List<RecordFieldAddSatelliteEditDto>();
        var mutationsByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filePath = declaringDoc.FilePath
            ?? throw new InvalidOperationException($"Declaring document for '{typeMetadataName}' has no file path.");
        var originalText = sourceText.ToString();
        mutationsByFile[filePath] = originalText;

        foreach (var pattern in analysis.InferredPattern)
        {
            var edit = SynthesizeEditForPattern(pattern, analysis, newField, filePath, sourceText);
            if (edit is null)
            {
                continue;
            }
            edits.Add(edit);

            // Apply the synthesized insertion into the accumulating file content so the
            // composite preview carries the full post-edit text. Edits are insert-only
            // so we re-derive offsets from the current snapshot for each pattern.
            var currentContent = mutationsByFile[filePath];
            var currentText = SourceText.From(currentContent);
            var offset = ComputeCharOffset(currentText, edit.Line, edit.Column);
            mutationsByFile[filePath] = currentContent.Insert(offset, edit.NewText);
        }

        if (edits.Count == 0)
        {
            return EmptyResult(typeSymbol, newField,
                "Pattern inferred but no insertion anchor could be located for any pattern kind.",
                "No satellite edits — pattern inferred but no insertion anchors resolved.");
        }

        var mutations = mutationsByFile
            .Select(kvp => new CompositeFileMutation(kvp.Key, kvp.Value))
            .ToList();

        var description = $"Add '{newField.Name}' ({newField.Type}) satellite edits: {edits.Count} site(s) across {mutations.Count} file(s).";
        var token = _compositePreviewStore.Store(workspaceId, _workspace.GetCurrentVersion(workspaceId), description, mutations);

        var summary =
            $"{edits.Count} satellite edit(s) spanning {string.Join(", ", analysis.InferredPattern)}.";

        return new RecordFieldAddSatelliteDto(
            TargetTypeDisplay: typeSymbol.ToDisplayString(),
            NewField: newField,
            InferredPattern: analysis.InferredPattern,
            PatternDetectionReason: string.Empty,
            ProposedEdits: edits,
            PreviewToken: token,
            Summary: summary);
    }

    private static RecordFieldAddSatelliteDto EmptyResult(
        INamedTypeSymbol typeSymbol,
        NewSatelliteFieldDto newField,
        string reason,
        string summary)
    {
        return new RecordFieldAddSatelliteDto(
            TargetTypeDisplay: typeSymbol.ToDisplayString(),
            NewField: newField,
            InferredPattern: Array.Empty<string>(),
            PatternDetectionReason: reason,
            ProposedEdits: Array.Empty<RecordFieldAddSatelliteEditDto>(),
            PreviewToken: null,
            Summary: summary);
    }

    private static IReadOnlyList<string> ExtractExistingFieldNames(INamedTypeSymbol typeSymbol)
    {
        // Include both properties (the canonical source-declared name for positional record
        // parameters and `{ get; init; }` properties) and instance fields. Skip compiler-
        // generated, static, and private-implementation members — the pattern cares about
        // the visible surface.
        var names = new List<string>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.IsStatic) continue;
            switch (member)
            {
                case IPropertySymbol prop when prop.DeclaredAccessibility == Accessibility.Public:
                    names.Add(prop.Name);
                    break;
                case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public &&
                                              !field.Name.StartsWith('<'):
                    names.Add(field.Name);
                    break;
            }
        }
        // Deduplicate while preserving declaration order — record primary ctor params also
        // surface as auto-properties in InstanceMembers, but GetMembers sometimes reports
        // backing fields. Canonical field-name list is what we need.
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeEditForPattern(
        string patternKind,
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        return patternKind switch
        {
            SatelliteKind.SnapshotTypeField => SynthesizeSnapshotFieldEdit(analysis, newField, filePath, sourceText),
            SatelliteKind.CloneMethodBody => SynthesizeCloneAssignmentEdit(analysis, newField, filePath, sourceText),
            SatelliteKind.WithMethodAssignment => SynthesizeWithAssignmentEdit(analysis, newField, filePath, sourceText),
            SatelliteKind.IncrementMethod => SynthesizeIncrementMethodEdit(analysis, newField, filePath, sourceText),
            SatelliteKind.ToJsonCase => SynthesizeToJsonCaseEdit(analysis, newField, filePath, sourceText),
            _ => null
        };
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeSnapshotFieldEdit(
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        var anchor = analysis.SnapshotFieldAnchors.LastOrDefault();
        if (anchor is null) return null;

        // Place the new mirror-type property on the line after the last existing sibling
        // declaration. Indentation is sampled from the anchor so the new line fits in.
        var indent = ExtractIndent(sourceText, anchor.StartLine);
        var newText = $"{indent}public {newField.Type} {newField.Name} {{ get; init; }}{Environment.NewLine}";
        var (insertLine, insertColumn) = ComputeAfterLine(sourceText, anchor.EndLine);
        return new RecordFieldAddSatelliteEditDto(
            FilePath: filePath,
            Line: insertLine,
            Column: insertColumn,
            NewText: newText,
            SiteKind: SatelliteKind.SnapshotTypeField,
            Description: $"Add '{newField.Name}' property to satellite mirror type '{anchor.ContainingTypeName}'.");
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeCloneAssignmentEdit(
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        var anchor = analysis.CloneAssignmentAnchors.LastOrDefault();
        if (anchor is null) return null;

        var indent = ExtractIndent(sourceText, anchor.StartLine);
        // Template: copy the existing-sibling assignment shape. Most Clone methods use
        // `FieldName = source.FieldName` (object-initializer) or `FieldName = FieldName`
        // (with-expression). Use object-initializer form for stability — it compiles in
        // both contexts.
        var newText = $"{indent}{newField.Name} = {analysis.CloneSourceExpression}.{newField.Name},{Environment.NewLine}";
        var (insertLine, insertColumn) = ComputeAfterLine(sourceText, anchor.EndLine);
        return new RecordFieldAddSatelliteEditDto(
            FilePath: filePath,
            Line: insertLine,
            Column: insertColumn,
            NewText: newText,
            SiteKind: SatelliteKind.CloneMethodBody,
            Description: $"Add '{newField.Name}' assignment to Clone method body.");
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeWithAssignmentEdit(
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        var anchor = analysis.WithAssignmentAnchors.LastOrDefault();
        if (anchor is null) return null;

        var indent = ExtractIndent(sourceText, anchor.StartLine);
        var newText = $"{indent}{newField.Name} = {analysis.WithSourceExpression}.{newField.Name},{Environment.NewLine}";
        var (insertLine, insertColumn) = ComputeAfterLine(sourceText, anchor.EndLine);
        return new RecordFieldAddSatelliteEditDto(
            FilePath: filePath,
            Line: insertLine,
            Column: insertColumn,
            NewText: newText,
            SiteKind: SatelliteKind.WithMethodAssignment,
            Description: $"Add '{newField.Name}' assignment to With method body.");
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeIncrementMethodEdit(
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        var anchor = analysis.IncrementMethodAnchors.LastOrDefault();
        if (anchor is null) return null;

        var indent = ExtractIndent(sourceText, anchor.StartLine);
        var bodyIndent = indent + "    ";
        var template = analysis.IncrementMethodTemplate ?? $"public void Increment{{0}}() => {{0}}++;";
        var methodBody = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            template,
            newField.Name);
        var newText = $"{indent}{methodBody}{Environment.NewLine}";
        var (insertLine, insertColumn) = ComputeAfterLine(sourceText, anchor.EndLine);
        _ = bodyIndent; // bodyIndent is reserved for future multi-line template expansion.
        return new RecordFieldAddSatelliteEditDto(
            FilePath: filePath,
            Line: insertLine,
            Column: insertColumn,
            NewText: newText,
            SiteKind: SatelliteKind.IncrementMethod,
            Description: $"Add 'Increment{newField.Name}' method mirroring existing sibling pattern.");
    }

    private static RecordFieldAddSatelliteEditDto? SynthesizeToJsonCaseEdit(
        SatelliteAnalysis analysis,
        NewSatelliteFieldDto newField,
        string filePath,
        SourceText sourceText)
    {
        var anchor = analysis.ToJsonCaseAnchors.LastOrDefault();
        if (anchor is null) return null;

        var indent = ExtractIndent(sourceText, anchor.StartLine);
        // We mirror the exact text of the last-observed sibling case, substituting the
        // field name. Authors usually have one line per case; this gives the reviewer
        // an edit that visually matches the existing pattern.
        var template = analysis.ToJsonCaseTemplate ?? "writer.WriteStartObject(nameof({0})); writer.WriteValue({0}); writer.WriteEndObject();";
        var bodyLine = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, newField.Name);
        var newText = $"{indent}{bodyLine}{Environment.NewLine}";
        var (insertLine, insertColumn) = ComputeAfterLine(sourceText, anchor.EndLine);
        return new RecordFieldAddSatelliteEditDto(
            FilePath: filePath,
            Line: insertLine,
            Column: insertColumn,
            NewText: newText,
            SiteKind: SatelliteKind.ToJsonCase,
            Description: $"Add '{newField.Name}' case to ToJson/Serialize method.");
    }

    private static string ExtractIndent(SourceText sourceText, int oneBasedLine)
    {
        if (oneBasedLine <= 0 || oneBasedLine > sourceText.Lines.Count) return string.Empty;
        var line = sourceText.Lines[oneBasedLine - 1];
        var text = line.ToString();
        var count = 0;
        while (count < text.Length && (text[count] == ' ' || text[count] == '\t')) count++;
        return text[..count];
    }

    private static (int Line, int Column) ComputeAfterLine(SourceText sourceText, int oneBasedLine)
    {
        // Insertion anchor is the start of the line *after* the target line (1-based).
        // This is where the NewText (which already ends in Environment.NewLine) gets
        // spliced — the result is the new line appears immediately below the anchor.
        var line = Math.Min(oneBasedLine + 1, sourceText.Lines.Count + 1);
        return (line, 1);
    }

    private static int ComputeCharOffset(SourceText sourceText, int oneBasedLine, int oneBasedColumn)
    {
        if (oneBasedLine > sourceText.Lines.Count) return sourceText.Length;
        var line = sourceText.Lines[oneBasedLine - 1];
        return line.Start + Math.Max(0, oneBasedColumn - 1);
    }

    /// <summary>
    /// Result of the satellite-site scan: per-field coverage sets, the inferred pattern
    /// (intersection of coverage sets when ≥2 fields agree), and per-kind anchors that the
    /// edit synthesizer uses to place new lines.
    /// </summary>
    private sealed record SatelliteAnalysis(
        IReadOnlyList<string> InferredPattern,
        string DetectionReason,
        IReadOnlyList<SatelliteAnchor> SnapshotFieldAnchors,
        IReadOnlyList<SatelliteAnchor> CloneAssignmentAnchors,
        IReadOnlyList<SatelliteAnchor> WithAssignmentAnchors,
        IReadOnlyList<SatelliteAnchor> IncrementMethodAnchors,
        IReadOnlyList<SatelliteAnchor> ToJsonCaseAnchors,
        string CloneSourceExpression,
        string WithSourceExpression,
        string? IncrementMethodTemplate,
        string? ToJsonCaseTemplate)
    {
        public static SatelliteAnalysis Analyze(
            SyntaxNode declaringRoot,
            INamedTypeSymbol targetType,
            IReadOnlyList<string> existingFields,
            SourceText sourceText)
        {
            // Per-field coverage: which satellite kinds did we observe the field participate in?
            var coverageByField = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var name in existingFields) coverageByField[name] = new HashSet<string>(StringComparer.Ordinal);

            var snapshotFieldAnchors = new List<SatelliteAnchor>();
            var cloneAnchors = new List<SatelliteAnchor>();
            var withAnchors = new List<SatelliteAnchor>();
            var incrementAnchors = new List<SatelliteAnchor>();
            var toJsonAnchors = new List<SatelliteAnchor>();

            var cloneSource = "source";
            var withSource = "source";
            string? incrementTemplate = null;
            string? toJsonTemplate = null;

            // 1) Mirror-type detection: sibling types in the same compilation unit whose name
            //    ends in Snapshot / Dto / Builder / Mirror / Projection AND whose member
            //    names overlap the target type's fields. Each such sibling contributes a
            //    SnapshotType.Field coverage entry for every field it declares.
            foreach (var typeDecl in declaringRoot.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (typeDecl.Identifier.ValueText == targetType.Name) continue;
                if (!LooksLikeMirrorType(typeDecl.Identifier.ValueText, targetType.Name)) continue;

                foreach (var member in typeDecl.Members)
                {
                    var memberName = GetMemberName(member);
                    if (memberName is null) continue;
                    if (!coverageByField.TryGetValue(memberName, out var coverage)) continue;

                    coverage.Add(SatelliteKind.SnapshotTypeField);
                    var lineSpan = member.GetLocation().GetLineSpan();
                    snapshotFieldAnchors.Add(new SatelliteAnchor(
                        StartLine: lineSpan.StartLinePosition.Line + 1,
                        EndLine: lineSpan.EndLinePosition.Line + 1,
                        ContainingTypeName: typeDecl.Identifier.ValueText,
                        FieldName: memberName));
                }
            }

            // 2) Method-level scans on the target type.
            var targetDecls = declaringRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(t => t.Identifier.ValueText == targetType.Name)
                .ToList();

            foreach (var typeDecl in targetDecls)
            {
                foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
                {
                    var name = method.Identifier.ValueText;
                    var isClone = string.Equals(name, "Clone", StringComparison.Ordinal)
                                   || name.StartsWith("Copy", StringComparison.Ordinal);
                    var isWith = name.StartsWith("With", StringComparison.Ordinal) && name != "WithCancellation";
                    var isIncrementForField = ExtractIncrementFieldName(name, existingFields);
                    var isToJsonLike = string.Equals(name, "ToJson", StringComparison.Ordinal)
                                        || string.Equals(name, "WriteJson", StringComparison.Ordinal)
                                        || string.Equals(name, "Serialize", StringComparison.Ordinal)
                                        || name.StartsWith("WriteTo", StringComparison.Ordinal);

                    if (isClone)
                    {
                        foreach (var name2 in existingFields)
                        {
                            var assignmentLine = FindAssignmentLineFor(method, name2);
                            if (assignmentLine is not null)
                            {
                                coverageByField[name2].Add(SatelliteKind.CloneMethodBody);
                                cloneAnchors.Add(assignmentLine);
                                var src = ExtractCloneSource(method);
                                if (!string.IsNullOrEmpty(src)) cloneSource = src!;
                            }
                        }
                    }
                    if (isWith)
                    {
                        foreach (var name2 in existingFields)
                        {
                            var assignmentLine = FindAssignmentLineFor(method, name2);
                            if (assignmentLine is not null)
                            {
                                coverageByField[name2].Add(SatelliteKind.WithMethodAssignment);
                                withAnchors.Add(assignmentLine);
                                var src = ExtractCloneSource(method);
                                if (!string.IsNullOrEmpty(src)) withSource = src!;
                            }
                        }
                    }
                    if (isIncrementForField is not null)
                    {
                        coverageByField[isIncrementForField].Add(SatelliteKind.IncrementMethod);
                        var lineSpan = method.GetLocation().GetLineSpan();
                        incrementAnchors.Add(new SatelliteAnchor(
                            StartLine: lineSpan.StartLinePosition.Line + 1,
                            EndLine: lineSpan.EndLinePosition.Line + 1,
                            ContainingTypeName: typeDecl.Identifier.ValueText,
                            FieldName: isIncrementForField));
                        incrementTemplate ??= BuildIncrementTemplate(method, isIncrementForField);
                    }
                    if (isToJsonLike)
                    {
                        foreach (var name2 in existingFields)
                        {
                            var caseAnchor = FindToJsonCaseLineFor(method, name2, sourceText);
                            if (caseAnchor is not null)
                            {
                                coverageByField[name2].Add(SatelliteKind.ToJsonCase);
                                toJsonAnchors.Add(caseAnchor);
                                toJsonTemplate ??= BuildToJsonTemplate(caseAnchor, name2, sourceText);
                            }
                        }
                    }
                }
            }

            // 3) Infer pattern: a kind belongs to "the pattern" only if ≥2 fields share it.
            var kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, coverage) in coverageByField)
            {
                foreach (var k in coverage)
                {
                    kindCounts[k] = kindCounts.TryGetValue(k, out var existing) ? existing + 1 : 1;
                }
            }

            // Ensure ≥2 sibling fields agree on the set — we also require those fields to
            // have *identical* coverage. A field that participates in (A, B) combined with
            // another that participates in (A, C) is a false-positive hazard: the shared
            // intersection {A} could be coincidence. The safer rule is: at least one pair
            // of fields has identical coverage, and the pattern is that intersection.
            var identicalPairs = FindFieldsWithIdenticalCoverage(coverageByField);

            if (identicalPairs.Count < 2)
            {
                // Fallback: if the kind-count says ≥2 fields share a given kind, that's a
                // safe signal even if their full coverage sets differ slightly. But we
                // scope the pattern to only kinds that ≥2 fields agreed on.
                var sharedKinds = kindCounts
                    .Where(kvp => kvp.Value >= 2)
                    .Select(kvp => kvp.Key)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                if (sharedKinds.Count == 0)
                {
                    var reason = coverageByField.Values.All(c => c.Count == 0)
                        ? "No satellite coverage detected for any existing field — the target type does not exhibit a mirror pattern."
                        : "Existing fields have divergent satellite coverage — no ≥2-field consensus to infer a pattern from.";
                    return new SatelliteAnalysis(
                        InferredPattern: Array.Empty<string>(),
                        DetectionReason: reason,
                        SnapshotFieldAnchors: snapshotFieldAnchors,
                        CloneAssignmentAnchors: cloneAnchors,
                        WithAssignmentAnchors: withAnchors,
                        IncrementMethodAnchors: incrementAnchors,
                        ToJsonCaseAnchors: toJsonAnchors,
                        CloneSourceExpression: cloneSource,
                        WithSourceExpression: withSource,
                        IncrementMethodTemplate: incrementTemplate,
                        ToJsonCaseTemplate: toJsonTemplate);
                }

                return new SatelliteAnalysis(
                    InferredPattern: sharedKinds,
                    DetectionReason: string.Empty,
                    SnapshotFieldAnchors: snapshotFieldAnchors,
                    CloneAssignmentAnchors: cloneAnchors,
                    WithAssignmentAnchors: withAnchors,
                    IncrementMethodAnchors: incrementAnchors,
                    ToJsonCaseAnchors: toJsonAnchors,
                    CloneSourceExpression: cloneSource,
                    WithSourceExpression: withSource,
                    IncrementMethodTemplate: incrementTemplate,
                    ToJsonCaseTemplate: toJsonTemplate);
            }

            // We have ≥2 fields with *identical* coverage — that shared set is the pattern.
            var inferred = identicalPairs[0].OrderBy(k => k, StringComparer.Ordinal).ToList();
            return new SatelliteAnalysis(
                InferredPattern: inferred,
                DetectionReason: string.Empty,
                SnapshotFieldAnchors: snapshotFieldAnchors,
                CloneAssignmentAnchors: cloneAnchors,
                WithAssignmentAnchors: withAnchors,
                IncrementMethodAnchors: incrementAnchors,
                ToJsonCaseAnchors: toJsonAnchors,
                CloneSourceExpression: cloneSource,
                WithSourceExpression: withSource,
                IncrementMethodTemplate: incrementTemplate,
                ToJsonCaseTemplate: toJsonTemplate);
        }

        private static List<HashSet<string>> FindFieldsWithIdenticalCoverage(
            Dictionary<string, HashSet<string>> coverageByField)
        {
            // Group fields by their coverage set (as a sorted-and-joined string). Any group
            // of size ≥2 contributes that coverage set to the "identical-coverage" list.
            var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, coverage) in coverageByField)
            {
                if (coverage.Count == 0) continue;
                var key = string.Join(",", coverage.OrderBy(k => k, StringComparer.Ordinal));
                groups[key] = coverage;
                counts[key] = counts.TryGetValue(key, out var existing) ? existing + 1 : 1;
            }
            return counts.Where(kvp => kvp.Value >= 2)
                .Select(kvp => groups[kvp.Key])
                .ToList();
        }

        private static bool LooksLikeMirrorType(string candidate, string targetName)
        {
            // Common suffixes for mirror types that sit alongside the target and intentionally
            // track its shape. The candidate must NOT equal the target name and MUST end with
            // one of these suffixes (case-sensitive).
            string[] suffixes = { "Snapshot", "Dto", "Builder", "Mirror", "Projection", "State" };
            if (!candidate.StartsWith(targetName, StringComparison.Ordinal) &&
                !suffixes.Any(s => candidate.EndsWith(s, StringComparison.Ordinal)))
            {
                return false;
            }
            return suffixes.Any(s => candidate.EndsWith(s, StringComparison.Ordinal));
        }

        private static string? GetMemberName(MemberDeclarationSyntax member)
        {
            return member switch
            {
                PropertyDeclarationSyntax prop => prop.Identifier.ValueText,
                FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
                _ => null
            };
        }

        private static SatelliteAnchor? FindAssignmentLineFor(MethodDeclarationSyntax method, string fieldName)
        {
            // Look for any assignment in the method body whose LHS names the field. Accept
            // three shapes because the plan's satellite patterns materialize in all of them:
            //   (a) `FieldName = expr` — bare identifier on LHS (object-initializer / direct
            //       self-assignment in With methods).
            //   (b) `target.FieldName = expr` — member-access on LHS (typical Clone body
            //       style, e.g. `result.A = source.A`).
            //   (c) `{ FieldName = expr }` — an AssignmentExpressionSyntax inside an
            //       InitializerExpressionSyntax (record/object initializer).
            // All three signal that the method mirrors the field.
            var body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
            if (body is null) return null;

            foreach (var node in body.DescendantNodes())
            {
                if (node is not AssignmentExpressionSyntax asn) continue;

                var matches = asn.Left switch
                {
                    IdentifierNameSyntax bareId => bareId.Identifier.ValueText == fieldName,
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == fieldName,
                    _ => false
                };

                if (matches)
                {
                    var lineSpan = node.GetLocation().GetLineSpan();
                    return new SatelliteAnchor(
                        StartLine: lineSpan.StartLinePosition.Line + 1,
                        EndLine: lineSpan.EndLinePosition.Line + 1,
                        ContainingTypeName: string.Empty,
                        FieldName: fieldName);
                }
            }
            return null;
        }

        private static string? ExtractCloneSource(MethodDeclarationSyntax method)
        {
            // Heuristic: if the method signature is `Clone(SomeType source)` use `source`;
            // if it's a parameterless `Clone()` with a body that references `this.*` use
            // `this`; otherwise default to `source`.
            var param = method.ParameterList.Parameters.FirstOrDefault();
            return param?.Identifier.ValueText ?? "this";
        }

        private static string? ExtractIncrementFieldName(string methodName, IReadOnlyList<string> existingFields)
        {
            const string prefix = "Increment";
            if (!methodName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var remainder = methodName[prefix.Length..];
            // Remove common suffix words like "Count" / "Failures" only if the exact field
            // name doesn't match first.
            if (existingFields.Contains(remainder, StringComparer.Ordinal)) return remainder;
            return null;
        }

        private static string BuildIncrementTemplate(MethodDeclarationSyntax method, string exampleField)
        {
            // Reconstruct a template string of the form `public void Increment{0}() => {0}++;`
            // by replacing the exampleField occurrences in the method source with a format
            // slot. We take the trivia-free source so the template is single-line whenever
            // possible.
            var source = method.ToString().Trim();
            // Replace every exact-word occurrence of the field name with {0}. Guard against
            // substring matches inside longer identifiers by requiring boundaries.
            var rebuiltName = method.Identifier.ValueText.Replace(exampleField, "{0}", StringComparison.Ordinal);
            source = Regex.Replace(source, $@"\b{Regex.Escape(method.Identifier.ValueText)}\b", rebuiltName);
            source = Regex.Replace(source, $@"\b{Regex.Escape(exampleField)}\b", "{0}");
            return source;
        }

        private static SatelliteAnchor? FindToJsonCaseLineFor(
            MethodDeclarationSyntax method,
            string fieldName,
            SourceText sourceText)
        {
            // A ToJson/Serialize method that names the field can take several shapes — the
            // key signal is "the line mentions the field name as an identifier or string".
            // We anchor on the innermost statement that contains the field reference.
            var body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
            if (body is null) return null;

            foreach (var stmt in body.DescendantNodes().OfType<StatementSyntax>())
            {
                var tokens = stmt.DescendantTokens();
                if (tokens.Any(t => t.ValueText == fieldName))
                {
                    var lineSpan = stmt.GetLocation().GetLineSpan();
                    return new SatelliteAnchor(
                        StartLine: lineSpan.StartLinePosition.Line + 1,
                        EndLine: lineSpan.EndLinePosition.Line + 1,
                        ContainingTypeName: string.Empty,
                        FieldName: fieldName);
                }
            }
            return null;
        }

        private static string BuildToJsonTemplate(SatelliteAnchor anchor, string fieldName, SourceText sourceText)
        {
            // Reconstruct a format string from the anchor line. Replace the field-name token
            // occurrences with {0} so a downstream formatter can splice the new field name.
            if (anchor.StartLine <= 0 || anchor.StartLine > sourceText.Lines.Count) return "// {0}";
            var line = sourceText.Lines[anchor.StartLine - 1].ToString().TrimStart();
            return Regex.Replace(line, $@"\b{Regex.Escape(fieldName)}\b", "{0}");
        }
    }

    /// <summary>
    /// One satellite-site anchor: the line range (inclusive, 1-based) and which field/type
    /// it refers to. Edits are placed *after* <see cref="EndLine"/>.
    /// </summary>
    private sealed record SatelliteAnchor(
        int StartLine,
        int EndLine,
        string ContainingTypeName,
        string FieldName);
}
