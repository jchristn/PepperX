namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Threading.Tasks;
    using PepperX.Core.Enumeration;
    using Touchstone.Core;

    /// <summary>
    /// Verifies enumeration query parsing, clamping, and validation.
    /// </summary>
    public static class EnumerationQuerySuite
    {
        /// <summary>
        /// Build the enumeration query suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "EnumerationQuery",
                displayName: "Enumeration query",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("EnumerationQuery", "Defaults", "A default query has sane defaults", _ =>
                    {
                        EnumerationQuery q = new EnumerationQuery();
                        Check.Equal(100, q.MaxResults, "default max results");
                        Check.Equal(0, q.Skip, "default skip");
                        Check.Equal(EnumerationOrderEnum.CreatedDescending, q.Ordering, "default ordering");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EnumerationQuery", "MaxResultsClamp", "MaxResults clamps to 1..1000", _ =>
                    {
                        EnumerationQuery q = new EnumerationQuery();
                        q.MaxResults = 0;
                        Check.Equal(1, q.MaxResults, "clamp low");
                        q.MaxResults = 99999;
                        Check.Equal(1000, q.MaxResults, "clamp high");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EnumerationQuery", "FromQueryString", "Query string binding parses all fields", _ =>
                    {
                        NameValueCollection nvc = new NameValueCollection
                        {
                            { "maxResults", "50" },
                            { "skip", "10" },
                            { "ordering", "keyascending" },
                            { "prefix", "2026/" },
                            { "labels", "a, b ,c" },
                            { "tags", "team=mammals, tier=gold" },
                            { "caseInsensitive", "true" }
                        };
                        EnumerationQuery q = EnumerationQuery.FromQueryString(nvc);
                        Check.Equal(50, q.MaxResults, "maxResults");
                        Check.Equal(10, q.Skip, "skip");
                        Check.Equal(EnumerationOrderEnum.KeyAscending, q.Ordering, "ordering");
                        Check.Equal("2026/", q.Prefix!, "prefix");
                        Check.NotNull(q.Labels, "labels parsed");
                        Check.Equal(3, q.Labels!.Count, "label count");
                        Check.NotNull(q.Tags, "tags parsed");
                        Check.Equal("mammals", q.Tags!["team"], "tag value");
                        Check.True(q.CaseInsensitive, "case insensitive");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EnumerationQuery", "FromQueryStringBadValue", "Unparseable values throw", _ =>
                    {
                        NameValueCollection nvc = new NameValueCollection { { "maxResults", "abc" } };
                        Check.Throws<ArgumentException>(() => EnumerationQuery.FromQueryString(nvc), "bad maxResults");
                        NameValueCollection nvc2 = new NameValueCollection { { "tags", "novalue" } };
                        Check.Throws<ArgumentException>(() => EnumerationQuery.FromQueryString(nvc2), "bad tag pair");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EnumerationQuery", "ValidateMutualExclusion", "Skip and continuation token are mutually exclusive", _ =>
                    {
                        EnumerationQuery q = new EnumerationQuery { Skip = 5, ContinuationToken = "ext_abc" };
                        Check.False(q.Validate(out string? err), "should be invalid");
                        Check.NotNull(err, "error message present");

                        EnumerationQuery q2 = new EnumerationQuery { ContinuationToken = "ext_abc", Ordering = EnumerationOrderEnum.KeyAscending };
                        Check.False(q2.Validate(out string? err2), "token invalid with key ordering");
                        Check.NotNull(err2, "error message present");

                        EnumerationQuery q3 = new EnumerationQuery
                        {
                            CreatedAfterUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                            CreatedBeforeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        };
                        Check.False(q3.Validate(out string? err3), "after must precede before");
                        Check.NotNull(err3, "error message present");

                        EnumerationQuery valid = new EnumerationQuery { Skip = 5 };
                        Check.True(valid.Validate(out string? err4), "plain skip valid");
                        Check.True(err4 == null, "no error for valid query");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
