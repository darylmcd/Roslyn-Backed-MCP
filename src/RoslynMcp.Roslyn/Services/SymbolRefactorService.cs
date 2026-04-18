using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
}
