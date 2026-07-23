namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using PepperX.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Verifies settings clamping, file round-trip, and environment overrides.
    /// </summary>
    public static class SettingsSuite
    {
        /// <summary>
        /// Build the settings suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Settings",
                displayName: "Settings",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Settings", "Clamps", "Numeric settings clamp to their documented ranges", _ =>
                    {
                        RestSettings rest = new RestSettings();
                        rest.Port = 999999;
                        Check.Equal(65535, rest.Port, "port clamp high");
                        rest.Port = -1;
                        Check.Equal(1, rest.Port, "port clamp low");

                        StorageSettings storage = new StorageSettings();
                        storage.MaxObjectBytes = -100;
                        Check.Equal(1L, storage.MaxObjectBytes, "object bytes clamp low");

                        RespSettings resp = new RespSettings();
                        resp.DatabaseCount = 9999;
                        Check.Equal(256, resp.DatabaseCount, "database count clamp high");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Settings", "FileRoundtrip", "Settings survive save and load", _ =>
                    {
                        string path = Path.Combine(Path.GetTempPath(), "pepperx-settings-" + Guid.NewGuid().ToString("N") + ".json");
                        try
                        {
                            PepperXSettings original = new PepperXSettings();
                            original.Database.Hostname = "db.example.com";
                            original.Rest.Port = 9100;
                            SettingsManager.Save(original, path);

                            PepperXSettings loaded = SettingsManager.Load(path);
                            Check.Equal("db.example.com", loaded.Database.Hostname, "hostname persisted");
                            Check.Equal(9100, loaded.Rest.Port, "rest port persisted");
                        }
                        finally
                        {
                            if (File.Exists(path)) File.Delete(path);
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Settings", "EnvironmentOverride", "Environment variables override settings", _ =>
                    {
                        string key = "PEPPERX_REST_PORT";
                        string? prior = Environment.GetEnvironmentVariable(key);
                        try
                        {
                            Environment.SetEnvironmentVariable(key, "7777");
                            PepperXSettings settings = new PepperXSettings();
                            SettingsManager.ApplyEnvironmentOverrides(settings);
                            Check.Equal(7777, settings.Rest.Port, "rest port overridden from env");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(key, prior);
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Settings", "MissingFileCreatesDefault", "Loading a missing file writes defaults", _ =>
                    {
                        string path = Path.Combine(Path.GetTempPath(), "pepperx-default-" + Guid.NewGuid().ToString("N") + ".json");
                        try
                        {
                            Check.False(File.Exists(path), "precondition: file absent");
                            PepperXSettings settings = SettingsManager.Load(path);
                            Check.True(File.Exists(path), "default file created");
                            Check.Equal(8000, settings.Rest.Port, "default rest port");
                        }
                        finally
                        {
                            if (File.Exists(path)) File.Delete(path);
                        }
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
