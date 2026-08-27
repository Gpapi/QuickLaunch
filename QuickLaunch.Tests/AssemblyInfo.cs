using Xunit;

// Tests here contend for the same real resources: two of them walk the disk, several
// enumerate the shell, and one asserts a wall-clock budget. Run in parallel they measure
// the scheduler rather than the code, and the timing test fails on a machine that is
// merely busy with the rest of the suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
