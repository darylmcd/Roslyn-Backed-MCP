using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class TypeExtractionService : ITypeExtractionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public TypeExtractionService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewExtractTypeAsync(
        string workspaceId, string filePath, string sourceTypeName,
        IReadOnlyList<string> memberNames, string newTypeName, string? newFilePath,
        CancellationToken ct)
    {
        ValidateNewTypeName(newTypeName);
        if (memberNames.Count == 0)
            throw new ArgumentException("At least one member name must be specified.", nameof(memberNames));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var typeDecl = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, sourceTypeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{sourceTypeName}' not found in {filePath}.");

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve type '{sourceTypeName}'.");

        if (typeSymbol.DeclaringSyntaxReferences.Length > 1)
        {
            throw new InvalidOperationException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the source type has multiple " +
                "partial declarations, and the extraction cannot wire the composition field through constructors " +
                "declared in other parts. Consolidate the constructors into one declaration first, then retry.");
        }

        var (membersToExtract, _, analysisNodes) = PartitionMembers(typeDecl, memberNames, sourceTypeName);

        var blockingDependencies = CollectExtractTypeBlockingDependencies(semanticModel, typeSymbol, analysisNodes, ct);

        // BUG-005 (#2/#3): Refuse to generate code that the warnings prove will not compile.
        // The previous behavior emitted the warnings but still produced a preview that referenced
        // members staying on the source type, leading to broken builds when applied. Halting here
        // forces the caller to either include the missing members in the extraction or to redesign
        // the split before attempting it.
        if (blockingDependencies.Count > 0)
        {
            var summary = string.Join("; ", blockingDependencies.Select(dependency => dependency.Reason));
            // extract-type-preview-refusal-missing-blocking-deps: the prose message is unchanged;
            // the structured per-member blocking data that produced it now rides along on the
            // exception so callers can retry with a corrected memberNames set programmatically.
            throw new ExtractTypeBlockingDependencyException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the selected members reference state " +
                $"that would remain on the source type, so the generated code would not compile. " +
                $"Either include the referenced members in the extraction or perform a manual redesign first. " +
                $"Details: {summary}",
                blockingDependencies);
        }

        // dr-9-1-does-not-update-external-consumer-call-sites (SampleSolution audit §9.1):
        // If any extracted member is referenced from a file OUTSIDE the source file, applying
        // the extraction silently breaks those callers — the methods move to the new type but
        // the new type is constructor-injected as a private field on the source, so external
        // code calling `source.ExtractedMember()` no longer compiles. Refuse the preview so the
        // caller knows to either pull the affected callers into the extraction redesign, or to
        // first refactor those callers to interact with the new type directly via DI / factory.
        var externalConsumerWarnings = await CollectExternalConsumerWarningsAsync(
            solution, sourceDocument, semanticModel, analysisNodes, ct).ConfigureAwait(false);
        if (externalConsumerWarnings.Count > 0)
        {
            var summary = string.Join("; ", externalConsumerWarnings);
            throw new InvalidOperationException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the selected member(s) are " +
                $"referenced by external consumer files. Applying the extraction would move the members to the new " +
                $"type (constructor-injected as a private field on the source), breaking every external call site. " +
                $"Either include the calling code in the extraction redesign, or first refactor consumers to interact " +
                $"with the new type directly via DI / a public factory. Details: {summary}");
        }

        var identifierCore = newTypeName[0] == '@' ? newTypeName[1..] : newTypeName;
        var fieldName = "_" + char.ToLowerInvariant(identifierCore[0]) + identifierCore[1..];
        var rewrittenTypeDecl = RewriteSameFileConsumers(
            typeDecl,
            semanticModel,
            analysisNodes,
            newTypeName,
            fieldName,
            sourceTypeName,
            ct);
        var (_, rewrittenMembersToKeep, _) = PartitionMembers(rewrittenTypeDecl, memberNames, sourceTypeName);

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var resolvedTargetPath = newFilePath ?? Path.Combine(sourceDir, $"{newTypeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Build the new type declaration with extracted members
        var newFileRoot = BuildNewFileRoot(sourceRoot, typeDecl, membersToExtract, newTypeName);

        // Remove extracted members from source type and inject field + ctor parameter
        var updatedTypeDecl = InjectFieldAndCtorParameter(
            rewrittenTypeDecl.WithMembers(SyntaxFactory.List(rewrittenMembersToKeep)),
            typeDecl, semanticModel, newTypeName, fieldName, sourceTypeName, ct);

        // Replace in source root (normalize so field/modifier tokens get proper spacing)
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);
        if (updatedSourceRoot is CompilationUnitSyntax normalizedRoot)
            updatedSourceRoot = normalizedRoot.NormalizeWhitespace();

        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add new document. Item #1 — pass folders so MSBuildWorkspace's TryApplyChanges
        // resolves the disk path consistently with our explicit write.
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, resolvedTargetPath);
        var newDoc = targetProject.AddDocument(targetFileName, newFileRoot.ToFullString(), folders: folders, filePath: resolvedTargetPath);
        newSolution = newDoc.Project.Solution;

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {membersToExtract.Count} member(s) from '{sourceTypeName}' into new type '{newTypeName}'";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            PreviewKind.ExtractType);

        return new RefactoringPreviewDto(
            token, description, changes,
            Warnings: null);
    }

    private static void ValidateNewTypeName(string newTypeName)
    {
        try
        {
            IdentifierValidation.ThrowIfInvalidIdentifier(newTypeName, "newTypeName");
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException(exception.Message, nameof(newTypeName), exception);
        }
    }

    /// <summary>
    /// Splits the source type's members into the set that moves to the new type and the set that
    /// stays behind, plus the ORIGINAL, in-tree syntax nodes that downstream semantic analysis must
    /// run against (<c>AnalysisNodes</c>).
    /// <para>
    /// type-extraction-member-shape-validation: the extraction pipeline moves members by name, and
    /// three name shapes cannot be moved safely — a constructor (whose identifier is the source
    /// type's name), an ambiguous method-overload name, and one declarator of a multi-declarator
    /// field. The first two are refused up front by <see cref="ValidateRequestedMemberShapes"/>; the
    /// third is handled per declarator by <see cref="PartitionFieldDeclarators"/>.
    /// </para>
    /// <para>
    /// <c>AnalysisNodes</c> is a separate list because the extracted half of a split field is a
    /// synthesized node that belongs to no syntax tree, and <see cref="SemanticModel"/> rejects
    /// foreign nodes. Semantic passes therefore consume the original declarators, never the
    /// synthesized declaration.
    /// </para>
    /// </summary>
    private static (List<MemberDeclarationSyntax> ToExtract, List<MemberDeclarationSyntax> ToKeep, List<SyntaxNode> AnalysisNodes) PartitionMembers(
        TypeDeclarationSyntax typeDecl, IReadOnlyList<string> memberNames, string sourceTypeName)
    {
        var requestedNames = new HashSet<string>(memberNames, StringComparer.Ordinal);

        ValidateRequestedMemberShapes(typeDecl, requestedNames, sourceTypeName);

        var matchedNames = new HashSet<string>(StringComparer.Ordinal);
        var toExtract = new List<MemberDeclarationSyntax>();
        var toKeep = new List<MemberDeclarationSyntax>();
        var analysisNodes = new List<SyntaxNode>();

        foreach (var member in typeDecl.Members)
        {
            if (member is FieldDeclarationSyntax field)
            {
                PartitionFieldDeclarators(field, requestedNames, matchedNames, toExtract, toKeep, analysisNodes);
                continue;
            }

            var name = GetMemberName(member);
            if (name is not null && requestedNames.Contains(name))
            {
                toExtract.Add(member);
                analysisNodes.Add(member);
                matchedNames.Add(name);
            }
            else
            {
                toKeep.Add(member);
            }
        }

        // Every declaration carrying a requested name is now moved. The pre-pass above already
        // refused the only shapes where several declarations can legally share a name (overloads,
        // constructors), so this no longer silently drops later same-named declarations the way the
        // previous "remove on first match" loop did.
        var unmatchedNames = requestedNames.Where(name => !matchedNames.Contains(name)).ToList();
        if (unmatchedNames.Count > 0)
        {
            // extract-type-preview-refusal-missing-blocking-deps: prose message unchanged; the
            // unmatched names are also projected into structured blocking dependencies so the
            // caller can correct `memberNames` without parsing the sentence.
            var unmatched = unmatchedNames
                .Select(name => new BlockingDependencyDto(
                    name,
                    $"Member '{name}' not found in type '{sourceTypeName}'."))
                .ToList();
            throw new ExtractTypeBlockingDependencyException(
                $"Members not found in type '{sourceTypeName}': {string.Join(", ", unmatchedNames)}",
                unmatched);
        }

        if (toExtract.Count == 0)
            throw new InvalidOperationException("No members matched for extraction.");

        return (toExtract, toKeep, analysisNodes);
    }

    /// <summary>
    /// type-extraction-member-shape-validation: refuses the requested member names that this
    /// extraction cannot honour unambiguously, before any partitioning happens. Reuses the same
    /// <see cref="ExtractTypeBlockingDependencyException"/> / <see cref="BlockingDependencyDto"/>
    /// contract as the existing unmatched-name and dangling-reference refusals so callers face one
    /// structured refusal shape rather than three ad hoc ones.
    /// </summary>
    private static void ValidateRequestedMemberShapes(
        TypeDeclarationSyntax typeDecl, HashSet<string> requestedNames, string sourceTypeName)
    {
        var blocking = new List<BlockingDependencyDto>();

        // (1) Constructors. A constructor's identifier is ALWAYS the source type's name, so a
        // requested name matching it used to select the constructor and emit it verbatim inside
        // `public sealed class {newTypeName}` — where it is no longer a constructor at all but an
        // ill-formed method named after the old type (CS1520). Static constructors hit the same arm.
        foreach (var ctorName in typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .Select(c => c.Identifier.Text)
            .Where(requestedNames.Contains)
            .Distinct(StringComparer.Ordinal))
        {
            blocking.Add(new BlockingDependencyDto(
                ctorName,
                $"'{ctorName}' names a constructor of '{sourceTypeName}'. Constructors are never eligible for " +
                $"extraction: the new type is declared under a different name, so the moved declaration would " +
                $"stop being a constructor and would not compile. Drop it from memberNames and extract the " +
                $"members it initializes instead."));
        }

        // (2) Method overloads. `memberNames` carries bare names, so an overloaded name cannot
        // identify a single declaration. The previous loop extracted whichever overload came first
        // in source order and silently left the rest behind.
        foreach (var group in typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(m => requestedNames.Contains(m.Identifier.Text))
            .GroupBy(m => m.Identifier.Text, StringComparer.Ordinal))
        {
            var candidates = group.ToList();
            if (candidates.Count < 2)
                continue;

            // The defining and implementing halves of a `partial` method are ONE logical member,
            // not an overload set — both halves move together, so there is nothing ambiguous.
            if (candidates.All(m => m.Modifiers.Any(SyntaxKind.PartialKeyword)))
                continue;

            foreach (var candidate in candidates)
            {
                blocking.Add(new BlockingDependencyDto(
                    group.Key,
                    $"'{group.Key}' is ambiguous in '{sourceTypeName}': {candidates.Count} overloads declare that " +
                    $"name, including '{DescribeMethodSignature(candidate)}'. Extraction selects members by bare " +
                    $"name, so it cannot tell which overload was meant. Extract a name that resolves to exactly " +
                    $"one declaration, or move the specific overload by hand."));
            }
        }

        if (blocking.Count > 0)
        {
            throw new ExtractTypeBlockingDependencyException(
                $"Refusing to extract from '{sourceTypeName}': {string.Join("; ", blocking.Select(b => b.Reason))}",
                blocking);
        }
    }

    /// <summary>
    /// Renders a method's disambiguating signature. The type-parameter list is included so
    /// <c>Foo&lt;T&gt;(int)</c> and <c>Foo(int)</c> are reported as the distinct overloads they are.
    /// </summary>
    private static string DescribeMethodSignature(MethodDeclarationSyntax method)
    {
        return method.Identifier.Text
            + (method.TypeParameterList?.ToString() ?? string.Empty)
            + method.ParameterList.ToString();
    }

    /// <summary>
    /// type-extraction-member-shape-validation: a field declaration may declare several variables
    /// (<c>private int a, b, c;</c>) while <see cref="GetMemberName"/> only ever named the first, so
    /// requesting <c>a</c> silently dragged <c>b</c>/<c>c</c> along and requesting <c>b</c> matched
    /// nothing. Matching now happens per declarator: the node moves whole only when EVERY declarator
    /// was requested, otherwise it is split into an extracted half and a retained half that both
    /// preserve the original attributes, modifiers, type and initializers.
    /// <para>
    /// <c>EventFieldDeclarationSyntax</c> (<c>event EventHandler A, B;</c>) is deliberately NOT
    /// handled here — it derives from <c>BaseFieldDeclarationSyntax</c>, not
    /// <c>FieldDeclarationSyntax</c>, and <see cref="GetMemberName"/> never named it, so event fields
    /// remain unextractable exactly as before.
    /// </para>
    /// </summary>
    private static void PartitionFieldDeclarators(
        FieldDeclarationSyntax field,
        HashSet<string> requestedNames,
        HashSet<string> matchedNames,
        List<MemberDeclarationSyntax> toExtract,
        List<MemberDeclarationSyntax> toKeep,
        List<SyntaxNode> analysisNodes)
    {
        var declarators = field.Declaration.Variables;
        var requested = declarators.Where(v => requestedNames.Contains(v.Identifier.Text)).ToList();
        if (requested.Count == 0)
        {
            toKeep.Add(field);
            return;
        }

        foreach (var declarator in requested)
            matchedNames.Add(declarator.Identifier.Text);

        // Semantic analysis runs against the ORIGINAL in-tree nodes only: the declared type and
        // attributes of the field, plus the specific declarators that move. A synthesized split half
        // has no syntax tree, so passing it to the SemanticModel would throw; feeding only the
        // requested declarators also stops a retained sibling's initializer from producing a
        // dangling-reference refusal for state that never leaves the source type.
        analysisNodes.AddRange(field.AttributeLists);
        analysisNodes.Add(field.Declaration.Type);
        analysisNodes.AddRange(requested);

        if (requested.Count == declarators.Count)
        {
            toExtract.Add(field);
            return;
        }

        var retained = declarators.Where(v => !requestedNames.Contains(v.Identifier.Text)).ToList();
        toExtract.Add(WithDeclarators(field, requested));
        toKeep.Add(WithDeclarators(field, retained));
    }

    private static FieldDeclarationSyntax WithDeclarators(
        FieldDeclarationSyntax field, IEnumerable<VariableDeclaratorSyntax> declarators)
    {
        return field.WithDeclaration(
            field.Declaration.WithVariables(SyntaxFactory.SeparatedList(declarators)));
    }

    /// <summary>
    /// Rebinds references from retained source members to the extracted member's new owner.
    /// References are matched by symbol identity, so overloads, static access, and method groups
    /// cannot be redirected by a same-spelled unrelated symbol. Extracted declarations themselves
    /// are skipped and retain their original intra-helper references.
    /// </summary>
    private static TypeDeclarationSyntax RewriteSameFileConsumers(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        IReadOnlyList<SyntaxNode> analysisNodes,
        string newTypeName,
        string fieldName,
        string sourceTypeName,
        CancellationToken ct)
    {
        var extractedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in analysisNodes)
        {
            if (!IsDeclarationNode(node)) continue;

            var symbol = semanticModel.GetDeclaredSymbol(node, ct);
            if (symbol is not null)
            {
                extractedSymbols.Add(symbol.OriginalDefinition);
            }
        }

        var rewriter = new SameFileConsumerRewriter(
            semanticModel,
            extractedSymbols,
            newTypeName,
            fieldName,
            sourceTypeName,
            ct);
        return (TypeDeclarationSyntax)rewriter.Visit(typeDecl)!;
    }

    private sealed class SameFileConsumerRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly IReadOnlySet<ISymbol> _extractedSymbols;
        private readonly string _newTypeName;
        private readonly string _fieldName;
        private readonly string _sourceTypeName;
        private readonly CancellationToken _ct;

        public SameFileConsumerRewriter(
            SemanticModel semanticModel,
            IReadOnlySet<ISymbol> extractedSymbols,
            string newTypeName,
            string fieldName,
            string sourceTypeName,
            CancellationToken ct)
        {
            _semanticModel = semanticModel;
            _extractedSymbols = extractedSymbols;
            _newTypeName = newTypeName;
            _fieldName = fieldName;
            _sourceTypeName = sourceTypeName;
            _ct = ct;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            _ct.ThrowIfCancellationRequested();
            if (node is MemberDeclarationSyntax or VariableDeclaratorSyntax)
            {
                var declared = _semanticModel.GetDeclaredSymbol(node, _ct);
                if (declared is not null && IsExtracted(declared))
                {
                    return node;
                }
            }

            return base.Visit(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!TryGetExtractedSymbol(node.Name, out var symbol))
            {
                return base.VisitMemberAccessExpression(node);
            }

            EnsureInstanceReferenceCanBeRewritten(node, symbol);
            var memberName = (SimpleNameSyntax)base.Visit(node.Name)!;
            ExpressionSyntax owner;
            if (symbol.IsStatic)
            {
                owner = SyntaxFactory.IdentifierName(_newTypeName);
            }
            else
            {
                var receiver = (ExpressionSyntax)base.Visit(node.Expression)!;
                owner = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver.WithoutTrivia(),
                    SyntaxFactory.IdentifierName(_fieldName));
            }

            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    owner,
                    memberName.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            => RewriteUnqualifiedName(node) ?? base.VisitIdentifierName(node);

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
            => RewriteUnqualifiedName(node) ?? base.VisitGenericName(node);

        private ExpressionSyntax? RewriteUnqualifiedName(SimpleNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax { Name: var name } && name == node)
            {
                return null;
            }

            if (!TryGetExtractedSymbol(node, out var symbol))
            {
                return null;
            }

            if (node.Parent is MemberBindingExpressionSyntax)
            {
                throw CannotRewrite(symbol, "conditional-access receiver");
            }

            EnsureInstanceReferenceCanBeRewritten(node, symbol);
            var owner = symbol.IsStatic ? _newTypeName : _fieldName;
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(owner),
                    node.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        private bool TryGetExtractedSymbol(SimpleNameSyntax node, out ISymbol symbol)
        {
            var resolved = _semanticModel.GetSymbolInfo(node, _ct).Symbol;
            if (resolved is not null && IsExtracted(resolved))
            {
                symbol = resolved;
                return true;
            }

            symbol = null!;
            return false;
        }

        private bool IsExtracted(ISymbol symbol)
            => _extractedSymbols.Contains(symbol.OriginalDefinition);

        private void EnsureInstanceReferenceCanBeRewritten(SyntaxNode node, ISymbol symbol)
        {
            if (symbol.IsStatic)
            {
                return;
            }

            if (node.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().Any())
            {
                throw CannotRewrite(symbol, "constructor body before the injected composition field is assigned");
            }
        }

        private InvalidOperationException CannotRewrite(ISymbol symbol, string reason)
            => new(
                $"Refusing to extract type '{_newTypeName}' from '{_sourceTypeName}': retained same-file code " +
                $"references extracted member '{symbol.Name}' through a {reason}, which cannot be rebound without " +
                "changing behavior. Refactor that reference first, then retry the extraction.");
    }

    private static CompilationUnitSyntax BuildNewFileRoot(
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax typeDecl,
        IReadOnlyList<MemberDeclarationSyntax> membersToExtract,
        string newTypeName)
    {
        // The new type is emitted as `public sealed class NewType` with NO base list, so any
        // inheritance-only modifiers on the extracted members (`override`, `virtual`, `abstract`,
        // `sealed`, `new`) become compile errors or meaningless noise. Strip them alongside the
        // access-modifier normalization. Tracked by
        // `dr-9-3-preserves-when-new-type-does-not-inherit-the-bas`.
        var extractedMembers = membersToExtract
            .Select(EnsurePublicAccessibility)
            .Select(StripInheritanceOnlyModifiers)
            .ToList();
        TypeDeclarationSyntax newTypeDecl = SyntaxFactory.ClassDeclaration(newTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithMembers(SyntaxFactory.List(extractedMembers));

        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        MemberDeclarationSyntax topLevelMember = namespaceDecl switch
        {
            FileScopedNamespaceDeclarationSyntax fileScopedNs =>
                SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl)),
            NamespaceDeclarationSyntax blockNs =>
                SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl)),
            _ => newTypeDecl
        };

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(sourceRoot.Usings)
            .WithMembers(SyntaxFactory.SingletonList(topLevelMember))
            .NormalizeWhitespace();

        // dr-9-5-strips-the-blank-line-between-namespace-and-clas:
        // `NormalizeWhitespace()` emits a single newline between a namespace declaration and
        // its first type member, collapsing the conventional blank line that standard C#
        // style (and the audit fixture) expects. Post-process to guarantee a blank line sits
        // between the namespace and the extracted type, matching the layout users author by
        // hand and the shape `dotnet format` / editorconfig defaults produce.
        return EnsureBlankLineBetweenNamespaceAndType(compilationUnit);
    }

    /// <summary>
    /// After a `NormalizeWhitespace()` pass, inject a blank line before the first type
    /// declaration that sits inside (or immediately after) a namespace declaration so the
    /// emitted file reads `namespace Foo;\n\npublic sealed class NewType` or
    /// `namespace Foo\n{\n    public sealed class NewType` — the standard C# layout.
    /// Called from <see cref="BuildNewFileRoot"/> only; safe on both file-scoped and block
    /// namespace shapes.
    /// </summary>
    private static CompilationUnitSyntax EnsureBlankLineBetweenNamespaceAndType(CompilationUnitSyntax root)
    {
        var blankLine = SyntaxFactory.EndOfLine(Environment.NewLine);

        // File-scoped namespace: the type sits directly on the compilation unit's member
        // list after the namespace declaration. Inject the blank line on the first type's
        // leading trivia.
        var fileScopedNs = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNs is not null)
        {
            var firstType = fileScopedNs.Members.OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (firstType is not null)
            {
                return root.ReplaceNode(firstType, PrependBlankLine(firstType, blankLine));
            }
            return root;
        }

        // Block namespace: type lives inside the namespace's Members list. Same injection —
        // prepend a blank line to the first type declaration's leading trivia.
        var blockNs = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNs is not null)
        {
            var firstType = blockNs.Members.OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (firstType is not null)
            {
                return root.ReplaceNode(firstType, PrependBlankLine(firstType, blankLine));
            }
        }

        return root;
    }

    private static TypeDeclarationSyntax PrependBlankLine(TypeDeclarationSyntax typeDecl, SyntaxTrivia blankLine)
    {
        var existing = typeDecl.GetLeadingTrivia();

        // Avoid double-injecting if the trivia already contains a blank line (two or more
        // consecutive end-of-line markers) at the leading position.
        if (existing.Count > 0 && existing[0].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            return typeDecl;
        }

        return typeDecl.WithLeadingTrivia(existing.Insert(0, blankLine));
    }

    /// <summary>
    /// Injects the <c>private readonly {NewType} _field</c> composition field and wires it through
    /// EVERY instance constructor of the source type (type-extraction-composition-constructor-coverage).
    /// <para>
    /// The previous implementation mutated only <c>FirstOrDefault()</c> constructor, which left the
    /// readonly field silently null on the implicit-constructor and overloaded-constructor paths,
    /// produced CS1729 on <c>this(...)</c>-chained constructors, and skipped the assignment entirely
    /// for expression-bodied constructors. The rewrite classifies the whole constructor topology:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Zero instance constructors: a <c>public {SourceType}({NewType} p)</c>
    /// constructor is synthesized that assigns the field.</description></item>
    /// <item><description>Root constructors (no initializer, or <c>base(...)</c>): the parameter is
    /// inserted before the first optional parameter (preserving the BUG-005 CS1737 fix per
    /// constructor) and the assignment is appended; expression-bodied roots are rewritten to a block
    /// body so the assignment has somewhere to live.</description></item>
    /// <item><description><c>this(...)</c>-chained constructors: the parameter is added and forwarded
    /// to the delegated constructor's argument list at the target's insertion ordinal — the root the
    /// chain terminates in owns the single write of the readonly field.</description></item>
    /// <item><description>Static constructors are never mutated.</description></item>
    /// </list>
    /// <para>
    /// Unsupported topologies REFUSE before any syntax is emitted (matching the refusal idiom used by
    /// the external-consumer guard) instead of shipping a preview whose applied result is silently
    /// broken: primary constructors (records / <c>class C(...)</c>), bodyless instance constructors
    /// (extern/partial), named arguments in a <c>this(...)</c> initializer, and delegation targets
    /// the semantic model cannot resolve.
    /// </para>
    /// <paramref name="originalTypeDecl"/> is the in-tree declaration <paramref name="semanticModel"/>
    /// can answer questions about; <paramref name="typeDecl"/> is the detached copy whose members have
    /// already been partitioned. Constructors are never extracted (see
    /// <see cref="ValidateRequestedMemberShapes"/>), so the two constructor sequences correspond 1:1.
    /// </summary>
    private static TypeDeclarationSyntax InjectFieldAndCtorParameter(
        TypeDeclarationSyntax typeDecl, TypeDeclarationSyntax originalTypeDecl,
        SemanticModel semanticModel, string newTypeName, string fieldName, string sourceTypeName,
        CancellationToken ct)
    {
        if (typeDecl.ParameterList is not null)
        {
            throw new InvalidOperationException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the source type declares a " +
                $"primary constructor, and the extraction cannot wire the composition field through primary-constructor " +
                $"parameters. Convert the primary constructor to an explicit constructor first, then retry.");
        }

        var fieldDecl = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(newTypeName))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        var identifierCore = newTypeName[0] == '@' ? newTypeName[1..] : newTypeName;
        var camelCaseCore = char.ToLowerInvariant(identifierCore[0]) + identifierCore[1..];
        var paramName = newTypeName[0] == '@' ? "@" + camelCaseCore : camelCaseCore;
        var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
            .WithType(SyntaxFactory.ParseTypeName(newTypeName));
        var assignment = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(fieldName),
                SyntaxFactory.IdentifierName(paramName)));

        var ctors = typeDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var originalCtors = originalTypeDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var instanceIndexes = Enumerable.Range(0, ctors.Count)
            .Where(i => !ctors[i].Modifiers.Any(SyntaxKind.StaticKeyword))
            .ToList();

        if (instanceIndexes.Count == 0)
        {
            // Implicit constructor: the compiler-supplied parameterless constructor cannot assign the
            // new readonly field, so synthesize an explicit one that does. Inserted right after the
            // field; NormalizeWhitespace at the call site handles formatting.
            var synthesizedCtor = SyntaxFactory.ConstructorDeclaration(SyntaxFactory.Identifier(typeDecl.Identifier.Text))
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(newParam)))
                .WithBody(SyntaxFactory.Block(assignment));

            return typeDecl.WithMembers(typeDecl.Members.Insert(0, fieldDecl).Insert(1, synthesizedCtor));
        }

        // BUG-005 (#1), preserved per constructor: insert the new required parameter BEFORE any
        // optional or params parameters. Appending produced CS1737 ("required after optional") or
        // CS0231 ("params must be last"). This index is also the ordinal at which a chained
        // this(...) initializer must forward the new argument to its delegation target.
        static int ParameterInsertIndex(ConstructorDeclarationSyntax ctor)
        {
            var firstTrailingParameterIndex = ctor.ParameterList.Parameters.IndexOf(p =>
                p.Default is not null || p.Modifiers.Any(SyntaxKind.ParamsKeyword));
            return firstTrailingParameterIndex < 0
                ? ctor.ParameterList.Parameters.Count
                : firstTrailingParameterIndex;
        }

        var replacements = new Dictionary<ConstructorDeclarationSyntax, ConstructorDeclarationSyntax>();
        foreach (var i in instanceIndexes)
        {
            var ctor = ctors[i];
            if (ctor.Body is null && ctor.ExpressionBody is null)
            {
                throw new InvalidOperationException(
                    $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': constructor " +
                    $"'{ctor.Identifier.Text}({ctor.ParameterList.Parameters.Count} parameter(s))' has no body " +
                    $"(extern or partial), so the composition field cannot be assigned on that construction path. " +
                    $"Give the constructor a body first, then retry.");
            }

            var insertIndex = ParameterInsertIndex(ctor);
            var updatedCtor = ctor.WithParameterList(
                ctor.ParameterList.WithParameters(
                    ctor.ParameterList.Parameters.Insert(insertIndex, newParam)));

            if (ctor.Initializer is { } initializer && initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
            {
                // Chained constructor: forward the new argument to the delegated constructor — the
                // root the chain terminates in owns the single write of the readonly field, so no
                // assignment is added here. The argument's ordinal must match the position the
                // parameter was inserted at on the DELEGATION TARGET, which the semantic model
                // resolves against the original in-tree initializer.
                if (initializer.ArgumentList.Arguments.Any(a => a.NameColon is not null))
                {
                    throw new InvalidOperationException(
                        $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': a 'this(...)' constructor " +
                        $"initializer uses named arguments, so the forwarded '{paramName}' argument cannot be inserted " +
                        $"positionally. Convert the initializer to positional arguments first, then retry.");
                }

                var originalInitializer = originalCtors[i].Initializer!;
                var targetSymbol = semanticModel.GetSymbolInfo(originalInitializer, ct).Symbol as IMethodSymbol;
                var targetCtorIndex = targetSymbol is null ? -1 : originalCtors.FindIndex(c =>
                    targetSymbol.DeclaringSyntaxReferences.Any(r =>
                        r.Span == c.Span &&
                        string.Equals(r.SyntaxTree.FilePath, c.SyntaxTree.FilePath, StringComparison.Ordinal)));
                if (targetCtorIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the delegation target of a " +
                        $"'this(...)' constructor initializer could not be resolved, so the forwarded '{paramName}' " +
                        $"argument cannot be placed safely. Fix the constructor chain so it compiles, then retry.");
                }

                var arguments = initializer.ArgumentList.Arguments;
                var argumentIndex = Math.Min(ParameterInsertIndex(ctors[targetCtorIndex]), arguments.Count);
                updatedCtor = updatedCtor.WithInitializer(
                    initializer.WithArgumentList(
                        initializer.ArgumentList.WithArguments(
                            arguments.Insert(argumentIndex, SyntaxFactory.Argument(SyntaxFactory.IdentifierName(paramName))))));
            }
            else if (ctor.ExpressionBody is { } expressionBody)
            {
                // Expression-bodied root: rewrite to a block body so the assignment has a home —
                // the old `Body is not null` guard silently skipped these, leaving the field null.
                updatedCtor = updatedCtor
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(SyntaxFactory.Block(
                        SyntaxFactory.ExpressionStatement(expressionBody.Expression),
                        assignment));
            }
            else
            {
                updatedCtor = updatedCtor.WithBody(ctor.Body!.AddStatements(assignment));
            }

            replacements[ctor] = updatedCtor;
        }

        typeDecl = typeDecl.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
        return typeDecl.WithMembers(typeDecl.Members.Insert(0, fieldDecl));
    }

    /// <summary>
    /// dr-9-1-does-not-update-external-consumer-call-sites: For each member to be extracted,
    /// run a solution-wide reference search and collect any reference whose source-file path
    /// differs from the source document. Each external caller becomes a warning so the
    /// preview can refuse with an actionable message instead of silently producing a diff
    /// that the apply will break.
    /// </summary>
    private static async Task<List<string>> CollectExternalConsumerWarningsAsync(
        Solution solution,
        Document sourceDocument,
        SemanticModel semanticModel,
        IReadOnlyList<SyntaxNode> analysisNodes,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sourceFilePath = sourceDocument.FilePath
            ?? throw new InvalidOperationException("Source document must have a filesystem path.");

        foreach (var node in analysisNodes)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsDeclarationNode(node)) continue;

            var memberSymbol = semanticModel.GetDeclaredSymbol(node, ct);
            if (memberSymbol is null) continue;

            // Only public / internal members can be referenced from outside the source type
            // (private/protected references are confined to the source file or its derivatives,
            // which the existing dangling-reference check already handles).
            if (memberSymbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                continue;

            var references = await SymbolFinder.FindReferencesAsync(memberSymbol, solution, ct).ConfigureAwait(false);
            foreach (var refResult in references)
            {
                foreach (var loc in refResult.Locations)
                {
                    if (!loc.Location.IsInSource) continue;
                    var refDoc = solution.GetDocument(loc.Document.Id);
                    if (refDoc?.FilePath is null) continue;
                    if (IsSameFilePath(refDoc.FilePath, sourceFilePath))
                        continue;

                    var msg =
                        $"Extracted member '{memberSymbol.Name}' is referenced from external consumer " +
                        $"'{Path.GetFileName(refDoc.FilePath)}' (project '{refDoc.Project.Name}')";
                    if (seen.Add(msg))
                        warnings.Add(msg);
                }
            }
        }

        return warnings;
    }

    internal static bool IsSameFilePath(string firstPath, string secondPath)
    {
        ArgumentNullException.ThrowIfNull(firstPath);
        ArgumentNullException.ThrowIfNull(secondPath);
        return string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            FileSystemPath.Comparison);
    }

    /// <summary>
    /// Collects one entry per (extracted member, referenced symbol that stays behind) pair.
    /// extract-type-preview-refusal-missing-blocking-deps: returns structured
    /// <see cref="BlockingDependencyDto"/> values rather than flat prose so the refusal at the
    /// call site can carry them through to the caller. Dedup is keyed on the reason text alone
    /// (unchanged from the pre-structured behavior), so a symbol referenced from several
    /// extracted members is reported once, attributed to the first member that referenced it.
    /// </summary>
    private static List<BlockingDependencyDto> CollectExtractTypeBlockingDependencies(
        SemanticModel semanticModel,
        INamedTypeSymbol typeSymbol,
        IReadOnlyList<SyntaxNode> analysisNodes,
        CancellationToken ct)
    {
        var extractedDeclared = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var m in analysisNodes)
        {
            if (!IsDeclarationNode(m)) continue;

            var sym = semanticModel.GetDeclaredSymbol(m, ct);
            if (sym is not null)
                extractedDeclared.Add(sym);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var blockingDependencies = new List<BlockingDependencyDto>();

        foreach (var member in analysisNodes)
        {
            // AnalysisNodes deliberately includes non-declaration context nodes for split fields
            // (attribute lists and the declared type). Those nodes have no member name, so their
            // syntax kind is the load-bearing attribution fallback for a dependency found there.
            var memberName = GetAnalysisNodeName(member) ?? member.Kind().ToString();

            foreach (var node in member.DescendantNodesAndSelf())
            {
                ct.ThrowIfCancellationRequested();

                var sym = semanticModel.GetSymbolInfo(node, ct).Symbol;
                if (sym is null) continue;

                if (sym is ILocalSymbol or IParameterSymbol or ILabelSymbol)
                    continue;

                if (extractedDeclared.Contains(sym))
                    continue;

                if (!IsDeclaredInOrUnderType(sym, typeSymbol))
                    continue;

                if (sym is INamedTypeSymbol nt && SymbolEqualityComparer.Default.Equals(nt, typeSymbol))
                    continue;

                var msg =
                    $"Extracted member may reference '{sym.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' " +
                    $"which remains on the original type '{typeSymbol.Name}' and is not available in the new type.";
                if (seen.Add(msg))
                    blockingDependencies.Add(new BlockingDependencyDto(memberName, msg));
            }
        }

        return blockingDependencies;
    }

    private static bool IsDeclaredInOrUnderType(ISymbol sym, INamedTypeSymbol typeSymbol)
    {
        INamedTypeSymbol? t = sym.ContainingType;
        while (t is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(t, typeSymbol))
                return true;
            t = t.ContainingType;
        }

        return false;
    }

    /// <summary>
    /// True for the node kinds the semantic passes may hand to
    /// <c>SemanticModel.GetDeclaredSymbol</c>. The analysis list also carries non-declaration
    /// context nodes (a field's declared type and attribute lists) purely so their descendants get
    /// walked for dangling references.
    /// </summary>
    private static bool IsDeclarationNode(SyntaxNode node)
    {
        return node is MemberDeclarationSyntax or VariableDeclaratorSyntax;
    }

    /// <summary>
    /// Attribution label for an analysis node. A single declarator of a multi-declarator field is
    /// named by its own identifier so the structured refusal points at the exact variable the caller
    /// requested, not at the first declarator of the declaration that happens to contain it.
    /// </summary>
    private static string? GetAnalysisNodeName(SyntaxNode node)
    {
        return node switch
        {
            VariableDeclaratorSyntax v => v.Identifier.Text,
            MemberDeclarationSyntax m => GetMemberName(m),
            _ => null
        };
    }

    /// <summary>
    /// Maps a member declaration to the single name that selects it for extraction, or
    /// <see langword="null"/> when no such name exists.
    /// <para>
    /// type-extraction-member-shape-validation: fields and constructors are deliberately absent.
    /// A field can declare several names, so it is matched per declarator by
    /// <see cref="PartitionFieldDeclarators"/> instead of by its first declarator. A constructor's
    /// identifier is the SOURCE type's name, so naming one here made a request for the source type's
    /// own name select the constructor and emit it verbatim into the differently-named new type;
    /// <see cref="ValidateRequestedMemberShapes"/> refuses that request explicitly instead.
    /// </para>
    /// </summary>
    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            _ => null
        };
    }

    private static MemberDeclarationSyntax EnsurePublicAccessibility(MemberDeclarationSyntax member)
    {
        // Remove existing access modifiers and add public
        var accessModifiers = new[]
        {
            SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
            SyntaxKind.InternalKeyword
        };

        var currentModifiers = member switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            _ => default
        };

        var newModifiers = currentModifiers
            .Where(m => !accessModifiers.Contains(m.Kind()))
            .Prepend(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        var tokenList = SyntaxFactory.TokenList(newModifiers);

        return member switch
        {
            MethodDeclarationSyntax m => m.WithModifiers(tokenList),
            PropertyDeclarationSyntax p => p.WithModifiers(tokenList),
            FieldDeclarationSyntax f => f.WithModifiers(tokenList),
            EventDeclarationSyntax e => e.WithModifiers(tokenList),
            _ => member
        };
    }

    /// <summary>
    /// Strips modifiers that only make sense in the context of a base class or hidden member
    /// (<c>override</c>, <c>virtual</c>, <c>abstract</c>, <c>sealed</c>, <c>new</c>) from an
    /// extracted member. The new type has no base list, so these modifiers either fail to
    /// compile (CS0115 on <c>override</c>, CS0549 on <c>virtual</c>/<c>abstract</c> inside a
    /// sealed class) or silently hide nothing (<c>new</c>). Called after
    /// <see cref="EnsurePublicAccessibility"/> so both transforms compose.
    /// </summary>
    private static MemberDeclarationSyntax StripInheritanceOnlyModifiers(MemberDeclarationSyntax member)
    {
        var currentModifiers = GetMemberModifiers(member);
        if (currentModifiers.Count == 0)
        {
            return member;
        }

        var strippedModifiers = BuildStrippedModifierList(currentModifiers);
        if (strippedModifiers.Count == currentModifiers.Count)
        {
            return member;
        }

        // For members whose method body remains valid after dropping `abstract`, we need to
        // also ensure the declaration has a body (abstract members carry `;` instead). Roslyn
        // keeps a null body as an abstract-method shape; if we stripped `abstract` from a
        // method without a body we leave an invalid declaration. This cannot occur in the
        // current extract path (the source method already had a body to be extracted), but we
        // guard defensively so any future caller shape surfaces an explicit error instead of
        // silently emitting broken syntax.
        ValidateAbstractMethodHasBody(member, currentModifiers);
        return WithMemberModifiers(member, strippedModifiers);
    }

    private static SyntaxTokenList BuildStrippedModifierList(SyntaxTokenList currentModifiers)
    {
        var kept = currentModifiers.Where(tok => !IsInheritanceOnlyModifier(tok)).ToArray();
        if (kept.Length > 0)
        {
            // Preserve the leading trivia from the original first modifier so the declaration
            // does not collapse against the preceding newline when an override-only modifier sat
            // at the front of the list.
            kept[0] = kept[0].WithLeadingTrivia(currentModifiers[0].LeadingTrivia);
        }

        return SyntaxFactory.TokenList(kept);
    }

    private static void ValidateAbstractMethodHasBody(MemberDeclarationSyntax member, SyntaxTokenList currentModifiers)
    {
        if (member is not MethodDeclarationSyntax method || !currentModifiers.Any(IsAbstractModifier))
        {
            return;
        }

        if (method.Body is not null || method.ExpressionBody is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Cannot extract abstract member '{method.Identifier.Text}' into a non-inheriting type: the source has no body.");
    }

    private static SyntaxTokenList GetMemberModifiers(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            _ => default
        };
    }

    private static MemberDeclarationSyntax WithMemberModifiers(MemberDeclarationSyntax member, SyntaxTokenList tokenList)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.WithModifiers(tokenList),
            PropertyDeclarationSyntax p => p.WithModifiers(tokenList),
            FieldDeclarationSyntax f => f.WithModifiers(tokenList),
            EventDeclarationSyntax e => e.WithModifiers(tokenList),
            _ => member
        };
    }

    private static bool IsAbstractModifier(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.AbstractKeyword);
    }

    private static bool IsInheritanceOnlyModifier(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.OverrideKeyword)
            || token.IsKind(SyntaxKind.VirtualKeyword)
            || token.IsKind(SyntaxKind.AbstractKeyword)
            || token.IsKind(SyntaxKind.SealedKeyword)
            || token.IsKind(SyntaxKind.NewKeyword);
    }
}
