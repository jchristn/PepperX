namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using Touchstone.Core;

    /// <summary>
    /// Verifies request history data access, including bucketed summary gap-filling.
    /// </summary>
    public static class DatabaseRequestHistorySuite
    {
        /// <summary>
        /// Build the request history database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseRequestHistory",
                displayName: "Database: request history",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseRequestHistory", "CreateRead", "Create and read an entry with bodies", async (driver, ct) =>
                    {
                        RequestHistoryEntry entry = new RequestHistoryEntry
                        {
                            Method = "POST",
                            Path = "/t/" + Guid.NewGuid().ToString("N"),
                            Url = "http://localhost/x",
                            StatusCode = 201,
                            DurationMs = 12.5,
                            RequestBody = "{\"a\":1}",
                            RequestBodyBytes = 7,
                            RequestHeaders = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                        };
                        await driver.RequestHistory.CreateAsync(entry, ct);

                        RequestHistoryEntry? back = await driver.RequestHistory.ReadAsync(entry.Id, ct);
                        Check.NotNull(back, "entry read");
                        Check.Equal(201, back!.StatusCode, "status");
                        Check.Equal("{\"a\":1}", back.RequestBody!, "body");
                        Check.Equal("application/json", back.RequestHeaders["Content-Type"], "header");
                    }),

                    DbTest.Case("DatabaseRequestHistory", "EnumerateFilter", "Filter and page entries", async (driver, ct) =>
                    {
                        string marker = "/t/" + Guid.NewGuid().ToString("N") + "/";
                        for (int i = 0; i < 5; i++)
                        {
                            await driver.RequestHistory.CreateAsync(new RequestHistoryEntry
                            {
                                Method = i % 2 == 0 ? "GET" : "POST",
                                Path = marker + i,
                                Url = "http://localhost" + marker + i,
                                StatusCode = i == 0 ? 500 : 200,
                                DurationMs = 5
                            }, ct);
                        }

                        RequestHistoryFilter filter = new RequestHistoryFilter { PathContains = marker, Method = "get" };
                        RequestHistoryPage page = await driver.RequestHistory.EnumerateAsync(filter, ct);
                        Check.Equal(3L, page.TotalCount, "three GET entries");

                        RequestHistoryFilter failFilter = new RequestHistoryFilter { PathContains = marker, StatusCode = 500 };
                        RequestHistoryPage failPage = await driver.RequestHistory.EnumerateAsync(failFilter, ct);
                        Check.Equal(1L, failPage.TotalCount, "one failure");
                    }),

                    DbTest.Case("DatabaseRequestHistory", "SummaryGapFill", "Summary emits every bucket including empty ones", async (driver, ct) =>
                    {
                        DateTime from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        DateTime to = from.AddHours(1);
                        await driver.RequestHistory.CreateAsync(new RequestHistoryEntry
                        {
                            Method = "GET",
                            Path = "/sum/" + Guid.NewGuid().ToString("N"),
                            Url = "http://localhost/sum",
                            StatusCode = 200,
                            DurationMs = 10,
                            CreatedUtc = from.AddMinutes(5)
                        }, ct);

                        RequestHistoryFilter filter = new RequestHistoryFilter { FromUtc = from, ToUtc = to, BucketMinutes = 15 };
                        RequestHistorySummary summary = await driver.RequestHistory.SummarizeAsync(filter, ct);
                        Check.Equal(4, summary.Buckets.Count, "four 15-minute buckets over one hour");
                        long nonEmpty = 0;
                        foreach (RequestHistoryBucket b in summary.Buckets) if (b.SuccessCount + b.FailureCount > 0) nonEmpty++;
                        Check.Equal(1L, nonEmpty, "exactly one bucket has data");
                    }),

                    DbTest.Case("DatabaseRequestHistory", "DeleteAndPrune", "Delete, bulk delete, and prune", async (driver, ct) =>
                    {
                        string marker = "/del/" + Guid.NewGuid().ToString("N") + "/";
                        RequestHistoryEntry one = new RequestHistoryEntry { Method = "GET", Path = marker + "a", Url = "u", StatusCode = 200, DurationMs = 1 };
                        await driver.RequestHistory.CreateAsync(one, ct);
                        Check.True(await driver.RequestHistory.DeleteAsync(one.Id, ct), "single delete");

                        for (int i = 0; i < 3; i++)
                            await driver.RequestHistory.CreateAsync(new RequestHistoryEntry { Method = "GET", Path = marker + i, Url = "u", StatusCode = 200, DurationMs = 1 }, ct);
                        int deleted = await driver.RequestHistory.DeleteManyAsync(new RequestHistoryFilter { PathContains = marker }, ct);
                        Check.Equal(3, deleted, "bulk delete count");

                        RequestHistoryEntry old = new RequestHistoryEntry { Method = "GET", Path = marker + "old", Url = "u", StatusCode = 200, DurationMs = 1, CreatedUtc = DateTime.UtcNow.AddDays(-40) };
                        await driver.RequestHistory.CreateAsync(old, ct);
                        int pruned = await driver.RequestHistory.PruneAsync(DateTime.UtcNow.AddDays(-30), ct);
                        Check.True(pruned >= 1, "prune removed old entry");
                    })
                });
        }
    }
}
