namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies extent enumeration: label/tag AND filters, prefix, ordering, pagination, and continuation.
    /// </summary>
    public static class DatabaseEnumerationSuite
    {
        /// <summary>
        /// Build the enumeration database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseEnumeration",
                displayName: "Database: enumeration",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseEnumeration", "LabelsAnd", "Label filter uses AND semantics", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "both", 1, new List<string> { "red", "blue" }), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "redonly", 1, new List<string> { "red" }), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "blueonly", 1, new List<string> { "blue" }), ct);

                        EnumerationQuery query = new EnumerationQuery { Labels = new List<string> { "red", "blue" } };
                        EnumerationResult<Extent> result = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                        Check.Equal(1L, result.TotalRecords, "only the extent with both labels");
                        Check.Equal("both", result.Objects[0].Key, "correct extent");
                    }),

                    DbTest.Case("DatabaseEnumeration", "TagsAnd", "Tag filter uses AND semantics", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "match", 1, null, new Dictionary<string, string> { { "team", "a" }, { "tier", "gold" } }), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "partial", 1, null, new Dictionary<string, string> { { "team", "a" } }), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "wrong", 1, null, new Dictionary<string, string> { { "team", "a" }, { "tier", "silver" } }), ct);

                        EnumerationQuery query = new EnumerationQuery { Tags = new Dictionary<string, string> { { "team", "a" }, { "tier", "gold" } } };
                        EnumerationResult<Extent> result = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                        Check.Equal(1L, result.TotalRecords, "only the exact tag match");
                        Check.Equal("match", result.Objects[0].Key, "correct extent");
                    }),

                    DbTest.Case("DatabaseEnumeration", "CaseInsensitive", "Case-insensitive label matching", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "k", 1, new List<string> { "Animal" }), ct);
                        EnumerationQuery query = new EnumerationQuery { Labels = new List<string> { "animal" }, CaseInsensitive = true };
                        EnumerationResult<Extent> result = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                        Check.Equal(1L, result.TotalRecords, "case-insensitive match");
                    }),

                    DbTest.Case("DatabaseEnumeration", "Prefix", "Key prefix filter", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "2026/07/a", 1), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "2026/07/b", 1), ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "2025/01/c", 1), ct);

                        EnumerationQuery query = new EnumerationQuery { Prefix = "2026/07/" };
                        EnumerationResult<Extent> result = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                        Check.Equal(2L, result.TotalRecords, "two with prefix");
                    }),

                    DbTest.Case("DatabaseEnumeration", "ContinuationToken", "Paging by continuation token covers all records once", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        for (int i = 0; i < 10; i++) await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "k" + i, 1), ct);

                        HashSet<string> seen = new HashSet<string>();
                        string? token = null;
                        int pages = 0;
                        while (pages < 20)
                        {
                            EnumerationQuery query = new EnumerationQuery { MaxResults = 3, Ordering = EnumerationOrderEnum.CreatedDescending, ContinuationToken = token };
                            EnumerationResult<Extent> page = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                            foreach (Extent e in page.Objects) Check.True(seen.Add(e.Id), "no duplicate across pages");
                            pages++;
                            if (page.EndOfResults) break;
                            token = page.ContinuationToken;
                            Check.NotNull(token, "token present mid-enumeration");
                        }
                        Check.Equal(10, seen.Count, "all records seen exactly once");
                    }),

                    DbTest.Case("DatabaseEnumeration", "RemainingAndTotal", "TotalRecords and RecordsRemaining are consistent", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        for (int i = 0; i < 7; i++) await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "k" + i, 1), ct);
                        EnumerationQuery query = new EnumerationQuery { MaxResults = 3, Skip = 0 };
                        EnumerationResult<Extent> page = await driver.Extents.EnumerateAsync(c.Id, query, ct);
                        Check.Equal(7L, page.TotalRecords, "total");
                        Check.Equal(4L, page.RecordsRemaining, "remaining after first page");
                        Check.False(page.EndOfResults, "not end");
                    })
                });
        }
    }
}
