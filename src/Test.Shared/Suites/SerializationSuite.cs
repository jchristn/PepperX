namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;
    using PepperX.Core.Serialization;
    using Touchstone.Core;

    /// <summary>
    /// Verifies JSON serialization: round-trips, strict enum handling, and the freeform metadata object.
    /// </summary>
    public static class SerializationSuite
    {
        /// <summary>
        /// Build the serialization suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Serialization",
                displayName: "Serialization",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Serialization", "ContainerRoundtrip", "Container survives a serialize/deserialize round-trip", _ =>
                    {
                        PepperXSerializer serializer = new PepperXSerializer();
                        Container c = new Container { Name = "photos" };
                        c.Tags["team"] = "mammals";
                        c.ObjectCount = 7;

                        string json = serializer.SerializeJson(c)!;
                        Container back = serializer.DeserializeJson<Container>(json);
                        Check.Equal(c.Id, back.Id, "id preserved");
                        Check.Equal("photos", back.Name, "name preserved");
                        Check.Equal("mammals", back.Tags["team"], "tag preserved");
                        Check.Equal(7L, back.ObjectCount, "count preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Serialization", "EnumAsString", "Enums serialize as strings", _ =>
                    {
                        PepperXSerializer serializer = new PepperXSerializer();
                        ApiErrorResponse err = new ApiErrorResponse(ApiErrorEnum.NotFound, "missing", 404);
                        string json = serializer.SerializeJson(err)!;
                        Check.True(json.Contains("NotFound", StringComparison.Ordinal), "enum serialized as name");
                        ApiErrorResponse back = serializer.DeserializeJson<ApiErrorResponse>(json);
                        Check.Equal(ApiErrorEnum.NotFound, back.Error, "enum round-tripped");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Serialization", "FreeformObject", "The metadata object round-trips arbitrary JSON", _ =>
                    {
                        PepperXSerializer serializer = new PepperXSerializer();
                        Dictionary<string, object> nested = new Dictionary<string, object>
                        {
                            { "any", new List<object> { "json", "at", "all" } },
                            { "count", 3 },
                            { "flag", true }
                        };
                        ObjectMetadata meta = new ObjectMetadata
                        {
                            Key = "2026/07/cat.jpg",
                            ExtentId = "ext_test",
                            Object = nested,
                            HasMetadataObject = true
                        };

                        string json = serializer.SerializeJson(meta)!;
                        ObjectMetadata back = serializer.DeserializeJson<ObjectMetadata>(json);
                        Check.Equal("2026/07/cat.jpg", back.Key, "key preserved");
                        Check.True(back.HasMetadataObject, "flag preserved");
                        Check.NotNull(back.Object, "object preserved");
                        string reserialized = serializer.SerializeJson(back.Object)!;
                        Check.True(reserialized.Contains("json", StringComparison.Ordinal), "nested array preserved");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
