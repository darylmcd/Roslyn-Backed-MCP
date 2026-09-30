using Microsoft.VisualStudio.TestTools.UnitTesting;

// Class-level parallelization: different test classes run concurrently on separate
// threads; tests within the same class still run sequentially. TestBase forwards to
// an assembly-owned service fixture with synchronized initialization. Opt out only
// when a class mutates process-global state or a workspace shared with other classes;
// document that dependency beside the class attribute. Workers = 0 lets MSTest use
// Environment.ProcessorCount threads.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
