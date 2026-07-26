namespace Test.Shared.Suites
{
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the null-safety, clamping, and cross-field validation contract for cache settings (§5.3 of
    /// CACHING.md). Pure; no database required.
    /// </summary>
    public static class ContainerCacheSettingsValidationSuite
    {
        /// <summary>
        /// Build the cache settings validation suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ContainerCacheSettingsValidation",
                displayName: "Container cache settings validation",
                cases: new System.Collections.Generic.List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "Defaults", "Default settings match the documented defaults", _ =>
                    {
                        ContainerCacheSettings s = new ContainerCacheSettings();
                        Check.False(s.Enabled, "disabled by default");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, s.Policy, "LRU by default");
                        Check.Equal(1000, s.MaxObjects, "MaxObjects default");
                        Check.Equal(0L, s.MaxMemoryBytes, "MaxMemoryBytes default");
                        Check.Equal(10, s.EvictCount, "EvictCount default");
                        Check.Equal(1048576L, s.MaxCacheableObjectBytes, "MaxCacheableObjectBytes default");
                        Check.Equal(10_000_000, s.MaxObjectsCeiling, "MaxObjectsCeiling default");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "CreationDefault", "Creation default is enabled LRU with reasonable sizes (D9)", _ =>
                    {
                        ContainerCacheSettings s = ContainerCacheSettings.CreationDefault();
                        Check.True(s.Enabled, "creation default enabled");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, s.Policy, "creation default LRU");
                        Check.Equal(1000, s.MaxObjects, "creation default MaxObjects");
                        Check.Equal(10, s.EvictCount, "creation default EvictCount");
                        Check.Equal(268435456L, s.MaxMemoryBytes, "creation default memory cap");
                        Check.Equal(1048576L, s.MaxCacheableObjectBytes, "creation default ceiling");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "ParsePolicy", "Policy parsing falls back to LRU and never throws", _ =>
                    {
                        Check.Equal(CacheEvictionPolicyEnum.LRU, ContainerCacheSettings.ParsePolicy(null), "null -> LRU");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, ContainerCacheSettings.ParsePolicy(""), "empty -> LRU");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, ContainerCacheSettings.ParsePolicy("bogus"), "unknown -> LRU");
                        Check.Equal(CacheEvictionPolicyEnum.FIFO, ContainerCacheSettings.ParsePolicy("fifo"), "case-insensitive FIFO");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, ContainerCacheSettings.ParsePolicy("LRU"), "LRU");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "ClampMaxObjects", "MaxObjects clamps to 1..ceiling and re-clamps EvictCount", _ =>
                    {
                        ContainerCacheSettings s = new ContainerCacheSettings();
                        s.MaxObjects = -5;
                        Check.Equal(1, s.MaxObjects, "negative -> 1");
                        Check.Equal(1, s.EvictCount, "EvictCount re-clamped down to MaxObjects");

                        s.MaxObjects = 0;
                        Check.Equal(1, s.MaxObjects, "zero -> 1");

                        s.MaxObjectsCeiling = 100;
                        s.MaxObjects = 1_000_000;
                        Check.Equal(100, s.MaxObjects, "clamped to ceiling");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "ClampOtherFields", "Memory, evict, and ceiling clamp per §5.3", _ =>
                    {
                        ContainerCacheSettings s = new ContainerCacheSettings();
                        s.MaxMemoryBytes = -100;
                        Check.Equal(0L, s.MaxMemoryBytes, "negative memory -> 0");

                        s.MaxCacheableObjectBytes = -1;
                        Check.Equal(0L, s.MaxCacheableObjectBytes, "negative ceiling -> 0");

                        s.MaxObjects = 50;
                        s.EvictCount = 999;
                        Check.Equal(50, s.EvictCount, "EvictCount clamped to MaxObjects");
                        s.EvictCount = 0;
                        Check.Equal(1, s.EvictCount, "EvictCount floor 1");

                        s.MaxObjectsCeiling = -3;
                        Check.Equal(1, s.MaxObjectsCeiling, "ceiling floor 1");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "RequestToSettingsAgree", "Request ToSettings clamps identically to the model", _ =>
                    {
                        UpdateCacheSettingsRequest req = new UpdateCacheSettingsRequest
                        {
                            Enabled = true,
                            Policy = CacheEvictionPolicyEnum.FIFO,
                            MaxObjects = -10,
                            MaxMemoryBytes = -20,
                            EvictCount = 5000,
                            MaxCacheableObjectBytes = -1
                        };
                        ContainerCacheSettings s = req.ToSettings();
                        Check.Equal(1, s.MaxObjects, "MaxObjects clamped");
                        Check.Equal(0L, s.MaxMemoryBytes, "memory clamped");
                        Check.Equal(1, s.EvictCount, "EvictCount clamped to MaxObjects");
                        Check.Equal(0L, s.MaxCacheableObjectBytes, "ceiling clamped");
                        Check.Equal(CacheEvictionPolicyEnum.FIFO, s.Policy, "policy preserved");
                        Check.True(s.Enabled, "enabled preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "RequestValidate", "Request validation rejects clearly-invalid input pre-clamp", _ =>
                    {
                        UpdateCacheSettingsRequest bad = new UpdateCacheSettingsRequest { Enabled = true, MaxObjects = 10, EvictCount = 100 };
                        Check.False(bad.Validate(out string? e1), "EvictCount > MaxObjects rejected");
                        Check.NotNull(e1, "error message present");

                        UpdateCacheSettingsRequest bad2 = new UpdateCacheSettingsRequest { Enabled = true, MaxMemoryBytes = 500, MaxCacheableObjectBytes = 1000 };
                        Check.False(bad2.Validate(out string? _), "memory below ceiling rejected");

                        UpdateCacheSettingsRequest good = new UpdateCacheSettingsRequest { Enabled = true, MaxObjects = 100, EvictCount = 10, MaxMemoryBytes = 0, MaxCacheableObjectBytes = 1048576 };
                        Check.True(good.Validate(out string? _), "valid request accepted");

                        UpdateCacheSettingsRequest disabled = new UpdateCacheSettingsRequest { Enabled = false, MaxObjects = 0, EvictCount = 999 };
                        Check.True(disabled.Validate(out string? _), "disabled request always valid");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("ContainerCacheSettingsValidation", "BuildSignature", "Signature changes with shape and is stable otherwise", _ =>
                    {
                        ContainerCacheSettings a = new ContainerCacheSettings { Enabled = true, MaxObjects = 100 };
                        ContainerCacheSettings b = new ContainerCacheSettings { Enabled = true, MaxObjects = 100 };
                        Check.Equal(a.BuildSignature(), b.BuildSignature(), "identical settings -> identical signature");
                        b.MaxObjects = 200;
                        Check.True(a.BuildSignature() != b.BuildSignature(), "capacity change -> different signature");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
