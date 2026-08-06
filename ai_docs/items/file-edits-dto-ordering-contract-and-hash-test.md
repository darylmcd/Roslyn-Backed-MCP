# file-edits-dto-ordering-contract-and-hash-test — clarify FileEditsDto.Edits ordering contract and tighten its equality test

**row:** `file-edits-dto-ordering-contract-and-hash-test` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/FileEditsDto.cs:25` (`<param name="Edits">` doc still says "in any order")
- `src/RoslynMcp.Core/Models/FileEditsDto.cs:26` (no defensive copy of the caller's collection)
- `tests/RoslynMcp.Tests/FileEditsDtoTests.cs:92` (hash-inequality assertion pins an unguaranteed implementation detail)

## Acceptance

- [ ] `<param name="Edits">` states edits may be supplied in any application order but that element order participates in record equality/hash.
- [ ] `<remarks>` scopes the aliased-mutation-closed claim to DTO-mediated mutation (or the constructor takes a defensive copy, e.g. `Edits.ToArray()`).
- [ ] `GetHashCode_IsOrderSensitive_NotCountBased` no longer asserts hash *inequality* as a contract (HashCode/Ordinal string hashing are collision-permitted); keep the `Assert.AreNotEqual(forward, reversed)` order-sensitivity assertion.

## Evidence

- Code-quality review of PR #1149 (`core-dto-fileeditsdto-array-to-readonlylist`): the param doc at `FileEditsDto.cs:25` still says "in any order" while the new `Equals`/`GetHashCode` (`:47`, `:58-61`) and the new test at `FileEditsDtoTests.cs:88-95` make element order load-bearing for value equality — a reader following the doc would expect `{a,b} == {b,a}`, which is now false.

## Context

Spin-off from the `core-dto-fileeditsdto-array-to-readonlylist` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1149). Doc/test-only, no runtime-bug claim.
