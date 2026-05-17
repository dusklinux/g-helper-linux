// Tiny test framework. Each scenario is a static method returning bool;
// the runner orchestrates fresh state, prints results, and exits non-zero
// when any scenario fails. No xUnit, no MSTest - single binary, fast to
// run from CI or the dev loop.

using GHelper.Linux.Gpu;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.Tests;

public static class Harness
{
    public static int Passed;
    public static int Failed;
    public static readonly List<string> FailedNames = new();

    /// <summary>Per-test sandbox state. Disposed at scenario end.</summary>
    public sealed class Sandbox : IDisposable
    {
        public string TempRoot { get; }
        public string ConfigDir { get; }
        public FakeAsusWmi Wmi { get; }
        public FakePowerManager Power { get; }
        public GpuModeController Controller { get; }

        public Sandbox(string scenarioName)
        {
            // GHELPER_TEST_ROOT is consumed at static-ctor time of
            // GpuModeController, so we cannot vary it per scenario. Place
            // all scenarios under a shared root that was set before the
            // first SUT touch (see Program.Main).
            string root = Environment.GetEnvironmentVariable("GHELPER_TEST_ROOT")
                ?? throw new InvalidOperationException("GHELPER_TEST_ROOT not set - Program.Main must set it before any GpuModeController access");
            TempRoot = root;

            // Wipe every test path so each scenario starts clean.
            string etcGhelper = Path.Combine(TempRoot, "etc", "ghelper");
            string etcModprobe = Path.Combine(TempRoot, "etc", "modprobe.d");
            string etcUdev = Path.Combine(TempRoot, "etc", "udev", "rules.d");
            Directory.CreateDirectory(etcGhelper);
            Directory.CreateDirectory(etcModprobe);
            Directory.CreateDirectory(etcUdev);
            foreach (var d in new[] { etcGhelper, etcModprobe, etcUdev })
                foreach (var f in Directory.EnumerateFiles(d))
                    File.Delete(f);

            // Fresh per-scenario config directory.
            ConfigDir = Path.Combine(TempRoot, "config", scenarioName);
            if (Directory.Exists(ConfigDir)) Directory.Delete(ConfigDir, recursive: true);
            Directory.CreateDirectory(ConfigDir);
            AppConfig.ResetForTest(ConfigDir);

            Wmi = new FakeAsusWmi();
            Power = new FakePowerManager();
            Controller = new GpuModeController(Wmi, Power);
        }

        public void Dispose() { /* Per-scenario state is reset on next ctor */ }

        // Convenience helpers -------------------------------------------------

        public string ModprobePath => GpuModeController.ModprobeBlockPath;
        public string UdevPath => GpuModeController.UdevRemovePath;
        public string TriggerPath => GpuModeController.TriggerPath;
        public string BackendPath => GpuModeController.BackendPath;

        public bool ModprobePresent() => File.Exists(ModprobePath);
        public bool UdevPresent() => File.Exists(UdevPath);
        public bool TriggerPresent() => File.Exists(TriggerPath);
        public bool BackendPresent() => File.Exists(BackendPath);
        public string TriggerContent() => File.Exists(TriggerPath) ? File.ReadAllText(TriggerPath).Trim() : "";
        public string BackendContent() => File.Exists(BackendPath) ? File.ReadAllText(BackendPath).Trim() : "";

        public void WriteBlockArtifacts()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModprobePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(UdevPath)!);
            File.WriteAllText(ModprobePath, "# test fixture\n");
            File.WriteAllText(UdevPath, "# test fixture\n");
        }

        public void WriteTrigger(string mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TriggerPath)!);
            File.WriteAllText(TriggerPath, mode);
        }

        public void WriteBackend(string backend)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackendPath)!);
            File.WriteAllText(BackendPath, backend);
        }
    }

    public static void Scenario(string name, Action<Sandbox> body)
    {
        try
        {
            using var sb = new Sandbox(name);
            body(sb);
            Console.WriteLine($"  PASS  {name}");
            Passed++;
        }
        catch (AssertException ex)
        {
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
            Failed++;
            FailedNames.Add(name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR {name}: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Failed++;
            FailedNames.Add(name);
        }
    }

    public static void Assert(bool cond, string message)
    {
        if (!cond) throw new AssertException(message);
    }

    public static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!Equals(expected, actual))
            throw new AssertException($"{what}: expected {expected}, got {actual}");
    }
}

public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}
