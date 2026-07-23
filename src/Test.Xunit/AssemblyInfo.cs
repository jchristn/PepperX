// The fact-style and theory-style test classes execute the same shared Touchstone descriptors against one
// in-process PepperX server. Running both classes at once would double every descriptor concurrently, so
// collection parallelization is disabled for this assembly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
