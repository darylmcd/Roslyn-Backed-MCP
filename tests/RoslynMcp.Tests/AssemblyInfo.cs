using Microsoft.VisualStudio.TestTools.UnitTesting;

// Class-level parallelization: different test classes run concurrently on separate
// threads; tests within the same class still run sequentially. Classes that share
// mutable state through TestBase's static services (WorkspaceManager, ChangeTracker,
// PreviewStore, etc.) must opt out with a class-level [DoNotParallelize] attribute.
// Workers = 0 lets MSTest use Environment.ProcessorCount threads.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
