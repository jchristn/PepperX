namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies model validation: container naming, required identifiers, and numeric guards.
    /// </summary>
    public static class ModelValidationSuite
    {
        /// <summary>
        /// Build the model validation suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ModelValidation",
                displayName: "Model validation",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ModelValidation", "ContainerNameValid", "Valid container names are accepted", _ =>
                    {
                        Container c = new Container();
                        c.Name = "photos";
                        Check.Equal("photos", c.Name, "valid lowercase name");
                        c.Name = "my-bucket-01";
                        Check.Equal("my-bucket-01", c.Name, "valid hyphenated name");
                        Check.True(Container.IsValidName("resp0"), "resp0 should be valid");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ModelValidation", "ContainerNameInvalid", "Invalid container names are rejected", _ =>
                    {
                        Check.Throws<ArgumentException>(() => new Container { Name = "AB" }, "too short");
                        Check.Throws<ArgumentException>(() => new Container { Name = "Photos" }, "uppercase");
                        Check.Throws<ArgumentException>(() => new Container { Name = "has_underscore" }, "underscore");
                        Check.Throws<ArgumentException>(() => new Container { Name = "-leadinghyphen" }, "leading hyphen");
                        Check.Throws<ArgumentException>(() => new Container { Name = "trailinghyphen-" }, "trailing hyphen");
                        Check.Throws<ArgumentException>(() => new Container { Name = "has space" }, "space");
                        Check.False(Container.IsValidName(new string('a', 64)), "over 63 chars invalid");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ModelValidation", "RequiredIdentifiers", "Blank identifiers and keys are rejected", _ =>
                    {
                        Check.Throws<ArgumentNullException>(() => new Extent { Id = " " }, "blank extent id");
                        Check.Throws<ArgumentNullException>(() => new Extent { ContainerId = "" }, "empty container id");
                        Check.Throws<ArgumentNullException>(() => new Extent { Key = "" }, "empty key");
                        Check.Throws<ArgumentNullException>(() => new Container { Id = "" }, "empty container id");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ModelValidation", "NumericGuards", "Negative counters are rejected", _ =>
                    {
                        Check.Throws<ArgumentOutOfRangeException>(() => new Container { ObjectCount = -1 }, "negative object count");
                        Check.Throws<ArgumentOutOfRangeException>(() => new Container { TotalBytes = -5 }, "negative total bytes");
                        Check.Throws<ArgumentOutOfRangeException>(() => new Extent { SizeBytes = -1 }, "negative size");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ModelValidation", "NullCollectionsBecomeEmpty", "Assigning null collections yields empty, not null", _ =>
                    {
                        Container c = new Container { Tags = null! };
                        Check.NotNull(c.Tags, "tags never null");
                        Extent e = new Extent { Labels = null!, Tags = null! };
                        Check.NotNull(e.Labels, "labels never null");
                        Check.NotNull(e.Tags, "extent tags never null");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
