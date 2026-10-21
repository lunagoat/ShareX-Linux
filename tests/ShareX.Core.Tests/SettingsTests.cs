using Newtonsoft.Json;
using ShareX.HelpersLib;
using System.Collections.Concurrent;

internal static class SettingsTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("Settings interrupted save recovery", RecoverTemp));
        tests.Add(("Settings failed save cleans temp", FailedSave));
        tests.Add(("Settings concurrent save notifications", ConcurrentSave));
        tests.Add(("Settings backup round trip", Backup));
        tests.Add(("Settings invalid load path fallback", () =>
        {
            Program.Check(TestSettings.Load("\0").Value == 0, "Invalid path did not fall back to defaults.");
            return Task.CompletedTask;
        }));
    }

    private static Task RecoverTemp()
    {
        string path = Path.Combine(Program.Root, "recovery.json");
        File.WriteAllText(path, "{");
        File.WriteAllText(path + ".not-a-save.temp", "{\"Value\":99}");
        string older = path + "." + Guid.NewGuid().ToString("N") + ".temp";
        File.WriteAllText(older, "{\"Value\":12}");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-2));
        string newer = path + "." + Guid.NewGuid().ToString("N") + ".temp";
        File.WriteAllText(newer, "{\"Value\":42}");
        string incomplete = path + "." + Guid.NewGuid().ToString("N") + ".temp";
        File.WriteAllText(incomplete, "{");
        File.SetLastWriteTimeUtc(incomplete, DateTime.UtcNow.AddMinutes(1));
        Program.Check(TestSettings.Load(path).Value == 42, "Failed to recover newest complete temporary save.");
        Program.Check(TestSettings.Load(path, fallbackSupport: false).Value == 0, "Recovery bypass ignored.");
        File.WriteAllText(path, "{\"Value\":7}");
        Program.Check(TestSettings.Load(path).Value == 7, "Temporary save overrode valid primary settings.");
        return Task.CompletedTask;
    }

    private static Task FailedSave()
    {
        string path = Path.Combine(Program.Root, "failed.json");
        File.WriteAllText(path, "{\"Value\":7}");
        var settings = new TestSettings { FailSerialization = true };
        Program.Check(!settings.Save(path), "Throwing property should fail serialization.");
        Program.Check(Directory.GetFiles(Program.Root, "failed.json.*.temp").Length == 0, "Failed save left temporary file.");
        Program.Check(TestSettings.Load(path).Value == 7, "Failed save damaged primary settings.");
        return Task.CompletedTask;
    }

    private static async Task ConcurrentSave()
    {
        string first = Path.Combine(Program.Root, "first.json");
        string second = Path.Combine(Program.Root, "second.json");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var settings = new TestSettings { Entered = entered, Release = release };
        var reported = new ConcurrentDictionary<int, string>();
        settings.SettingsSaved += (_, path, _) => reported[Environment.CurrentManagedThreadId] = path;
        Task<bool> saveFirst = Task.Run(() =>
        {
            bool result = settings.Save(first);
            Program.Check(reported[Environment.CurrentManagedThreadId] == first, "First save reported a different operation's path.");
            return result;
        });
        Program.Check(entered.Wait(TimeSpan.FromSeconds(3)), "Serialization did not start.");
        Task<bool> saveSecond = Task.Run(() => settings.Save(second));
        // The old Save changes FilePath while the first save holds its serialization lock.
        // With the fix it cannot change FilePath until that first save finishes.
        SpinWait.SpinUntil(() => settings.FilePath == second, TimeSpan.FromMilliseconds(300));
        release.Set();
        Program.Check(await saveFirst && await saveSecond, "Concurrent save failed.");
        Program.Check(File.Exists(first) && File.Exists(second), "One save was redirected to another path.");
    }

    private static Task Backup()
    {
        string path = Path.Combine(Program.Root, "backup.json");
        string backup = Path.Combine(Program.Root, "backups");
        var settings = new TestSettings { Value = 1, CreateBackup = true, BackupFolder = backup };
        Program.Check(settings.Save(path), "Initial save failed.");
        settings.Value = 2;
        Program.Check(settings.Save(path), "Replacement save failed.");
        Program.Check(TestSettings.Load(path).Value == 2, "Primary settings not updated.");
        Program.Check(TestSettings.Load(Path.Combine(backup, "backup.json")).Value == 1, "Backup did not retain previous settings.");
        return Task.CompletedTask;
    }

    public sealed class TestSettings : SettingsBase<TestSettings>
    {
        [JsonIgnore] public bool FailSerialization;
        [JsonIgnore] public ManualResetEventSlim Entered;
        [JsonIgnore] public ManualResetEventSlim Release;
        private int value;
        public int Value
        {
            get
            {
                if (FailSerialization) throw new InvalidOperationException("Synthetic serialization failure.");
                Entered?.Set();
                if (Release != null && !Release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
                return value;
            }
            set => this.value = value;
        }
    }
}
