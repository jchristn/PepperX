namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every PepperX descriptor sequentially through the Touchstone executor in one NUnit test.
    /// </summary>
    [TestFixture]
    public sealed class PepperXNunitFactTests : TouchstoneNunitBase
    {
        /// <summary>
        /// Suites under test.
        /// </summary>
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return PepperXSuites.All; }
        }

        /// <summary>
        /// Execute all suites.
        /// </summary>
        /// <returns>Task.</returns>
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync().ConfigureAwait(false);
        }
    }
}
