# editorconfig-section-source-path-matching — Match editorconfig sections against the source path

**row:** `editorconfig-section-source-path-matching` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:161`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Disk option overlay applies each section only when its glob matches the requested source file relative to that .editorconfig file.
- [ ] Regression covers a path-qualified C# glob that should match one source directory but not another, while ordinary [*.cs] and [*] sections still apply.

## Evidence

- `SectionMatchesCSharp` accepts any section ending in `.cs` for every C# file, regardless of its path. `ParseEditorconfigCsKeys` uses that predicate for GetOptionsAsync overlay, so a path-qualified section can leak options into unrelated documents. This differs from the set-option duplicate-section writer issue.

### Source-path matcher corpus
Decidable inputs: literal header, absolute config directory and absolute source path. Pairs below use source paths relative to that fixed config directory. No filesystem existence, shell evaluation or inferred intent enters the predicate. Red=13 rejects unrelated sections; green=12 retains matching sections.
red: ["[src/*.cs]","other/A.cs"] => false
red: ["[src/*.cs]","src/nested/A.cs"] => false
red: ["[*.{cs,csx,cake}]","src/A.vb"] => false
red: ["[A?.cs]","A12.cs"] => false
red: ["[A[!12].cs]","A1.cs"] => false
red: ["[A{1..3}.cs]","A4.cs"] => false
red: ["[*.csx]","A.cs"] => false
red: ["[*.cake]","A.cs"] => false
red: ["[*.vb]","A.cs"] => false
red: ["not-a-section","A.cs"] => false
red: ["[src/*.cs]","Src/A.cs"] => false
red: ["[*.CS]","A.cs"] => false
red: ["[file\\*.cs]","file/A.cs"] => false
green: ["[src/*.cs]","src/A.cs"] => true
green: ["[*.cs]","src/A.cs"] => true
green: ["[*]","src/A.cs"] => true
green: ["[/src/*.cs]","src/A.cs"] => true
green: ["[src/**.cs]","src/nested/A.cs"] => true
green: ["[**/*.cs]","src/nested/A.cs"] => true
green: ["[*.{cs,csx,cake}]","src/A.cs"] => true
green: ["[A?.cs]","A1.cs"] => true
green: ["[A[12].cs]","A1.cs"] => true
green: ["[A{1..3}.cs]","A2.cs"] => true
green: ["[A*]","A.cs"] => true
green: ["[{src,other}/A*]","src/A.cs"] => true
Measured baseline: exact current SectionMatchesCSharp lines 174-217 compiled by PowerShell Add-Type and invoked on all tuples at head 6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4. True-as-match TP=10 FP=11 FN=2 TN=2; rejection-as-positive TP=2 FP=2 FN=11 TN=10. Required repaired true-as-match TP=12 FP=0 FN=0 TN=13. Extend before implementation approval with escaped literal wildcard, whitespace within names, malformed patterns, zero-depth ** semantics from installed Roslyn, and absolute backslash-source forms; remeasure expanded denominators.
Official implementation inspected: https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfig.SectionNameMatching.cs (ordinal regex, slash-prefix rules, escapes, classes, nested alternatives, numeric ranges); https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfigSet.cs (normalized source separators/drive letters, ordinal containment, root/later-section precedence, severity separation); https://spec.editorconfig.org/ . Installed 5.9.0 XML confirms public Parse/GetOptionsForSourcePath; NamedSections/matcher are internal, not a supported reuse seam. Match provider semantics when specification and installed Roslyn differ; do not introduce a second grammar.

