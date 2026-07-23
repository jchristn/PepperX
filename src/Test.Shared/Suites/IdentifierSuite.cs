namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Verifies identifier generation: prefixes, uniqueness, and K-sortable ordering.
    /// </summary>
    public static class IdentifierSuite
    {
        /// <summary>
        /// Build the identifier suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Identifier",
                displayName: "Identifier generation",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Identifier", "Prefixes", "Generated IDs carry the correct prefixes", _ =>
                    {
                        Check.True(IdGenerator.GenerateContainerId().StartsWith(Constants.ContainerIdPrefix, StringComparison.Ordinal), "container prefix");
                        Check.True(IdGenerator.GenerateExtentId().StartsWith(Constants.ExtentIdPrefix, StringComparison.Ordinal), "extent prefix");
                        Check.True(IdGenerator.GenerateNodeId().StartsWith(Constants.NodeIdPrefix, StringComparison.Ordinal), "node prefix");
                        Check.True(IdGenerator.GenerateLeaseId().StartsWith(Constants.LeaseIdPrefix, StringComparison.Ordinal), "lease prefix");
                        Check.True(IdGenerator.GenerateRequestHistoryId().StartsWith(Constants.RequestHistoryIdPrefix, StringComparison.Ordinal), "request history prefix");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Identifier", "Length", "Generated IDs have the configured length", _ =>
                    {
                        Check.Equal(Constants.IdLength, IdGenerator.GenerateContainerId().Length, "id length");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Identifier", "Uniqueness", "Generated IDs are unique across many calls", _ =>
                    {
                        HashSet<string> seen = new HashSet<string>();
                        for (int i = 0; i < 10000; i++)
                        {
                            string id = IdGenerator.GenerateExtentId();
                            Check.True(seen.Add(id), "duplicate id generated: " + id);
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Identifier", "KSortable", "IDs generated later sort after IDs generated earlier", async ct =>
                    {
                        string first = IdGenerator.GenerateExtentId();
                        await Task.Delay(5, ct).ConfigureAwait(false);
                        string second = IdGenerator.GenerateExtentId();
                        Check.True(String.CompareOrdinal(first, second) < 0, "k-sortable ordering: '" + first + "' should sort before '" + second + "'");
                    })
                });
        }
    }
}
