---
category: Fixed
---

- **Fixed:** `find_type_mutations` now covers collection-field writes inside async lifecycle methods — `MutationAnalysisService.HasMutatingCollectionCall` recognizes `TryAdd`/`TryRemove`/`TryUpdate`/`AddOrUpdate`/`GetOrAdd`/`Push`/`Pop`/`Enqueue`/`Dequeue`/set-ops and indexer-write assignments (`field[key] = value`) alongside the previous `List<T>` surface, so `WorkspaceManager.LoadAsync`/`ReloadAsync`/`Close` (and similar ConcurrentDictionary-style mutators) surface as mutators instead of being invisible (`find-type-mutations-undercounts-lifecycle-writes`).
