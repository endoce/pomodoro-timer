using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;
using PomodoroTimer.Spike.Services;

internal static class PortableTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Portable production root is AppContext.BaseDirectory", () =>
        {
            Equal(Path.GetFullPath(AppContext.BaseDirectory), PortablePaths.Default.ApplicationRoot);
            Equal(Path.Combine(AppContext.BaseDirectory, "Data", "settings.json"), PortablePaths.Default.SettingsFile);
            Equal(Path.Combine(AppContext.BaseDirectory, "Data", "state.json"), PortablePaths.Default.StateFile);
            Equal(PortablePaths.Default.LogsDirectory, ApplicationLog.DirectoryPath);
            Equal(typeof(ApplicationLog), typeof(SpikeLog));
            Throws<ArgumentException>(() => new PortablePaths("relative"));
        });
        test("Portable Chinese and space paths perform real settings I/O", () =>
        {
            using var box = new Sandbox("中文 番茄计时器");
            var store = box.Settings();
            store.Save(new());
            Equal(1, store.Load().Value.SchemaVersion);
            Equal(true, File.Exists(box.Paths.SettingsFile));
        });
        test("CurrentDirectory changes neither existing nor default paths", () =>
        {
            using var box = new Sandbox();
            var old = Environment.CurrentDirectory;
            var settingsFile = box.Paths.SettingsFile;
            var production = PortablePaths.Default.ApplicationRoot;
            try
            {
                Environment.CurrentDirectory = box.Root;
                Equal(settingsFile, box.Paths.SettingsFile);
                Equal(production, new PortablePaths(AppContext.BaseDirectory).ApplicationRoot);
                box.Settings().Save(new());
                Equal(false, File.Exists(Path.Combine(box.Root, "settings.json")));
            }
            finally { Environment.CurrentDirectory = old; }
        });
        test("Data and Logs are created with no remaining write probes", () =>
        {
            using var box = new Sandbox(initialize: false);
            Equal(false, Directory.Exists(box.Paths.DataDirectory));
            box.Paths.Initialize(box.Diagnostics.Add);
            Equal(true, Directory.Exists(box.Paths.DataDirectory));
            Equal(true, Directory.Exists(box.Paths.LogsDirectory));
            Equal(0, Directory.GetFiles(box.Root, "*.tmp", SearchOption.AllDirectories).Length);
        });
        test("Log paths are culture independent", () =>
        {
            using var box = new Sandbox();
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                Equal("pomodoro-2026-11-11-0003.log", Path.GetFileName(box.Paths.LogFile(new(2026, 11, 11), 3)));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        test("Missing settings return validated defaults without writing", () =>
        {
            using var box = new Sandbox();
            var read = box.Settings().Load();
            Equal(new ApplicationSettings(), read.Value);
            Equal(true, read.CanSave);
            Equal(false, read.FromBackup);
            Equal(false, File.Exists(box.Paths.SettingsFile));
        });
        test("Settings roundtrip only persists the existing schema field", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings();
            store.Save(new());
            using var json = JsonDocument.Parse(File.ReadAllText(box.Paths.SettingsFile));
            Equal(1, json.RootElement.EnumerateObject().Count());
            Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            store.Save(store.Load().Value);
            Equal(true, File.Exists(store.BackupFile));
            Equal(new ApplicationSettings(), new SettingsStore(box.Paths, box.Diagnostics.Add).Load().Value);
        });
        test("Invalid settings input cannot overwrite a valid file", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new());
            var original = File.ReadAllText(box.Paths.SettingsFile);
            Throws<PersistenceException>(() => store.Save(new() { SchemaVersion = 0 }));
            Equal(original, File.ReadAllText(box.Paths.SettingsFile));
            Equal(true, box.Diagnostics.Any(d => d.Event == "storage.save.failed" && d.Exception != null));
        });
        test("Corrupt settings remain untouched and saving is refused", () =>
        {
            using var box = new Sandbox();
            const string corrupt = "{中文 broken";
            File.WriteAllText(box.Paths.SettingsFile, corrupt);
            var store = box.Settings(); var read = store.Load();
            Equal(false, read.CanSave); Equal(false, read.FromBackup);
            Throws<PersistenceException>(() => store.Save(new()));
            Equal(corrupt, File.ReadAllText(box.Paths.SettingsFile));
            Equal(true, read.Diagnostics.Any(d => d.Level == DiagnosticLevel.Error));
        });
        test("Malformed schema and unknown or duplicate fields are diagnosed", () =>
        {
            using var box = new Sandbox();
            var documents = new[] { "null", "[]", "{}", "{\"schemaVersion\":\"1\"}",
                "{\"schemaVersion\":null}", "{\"schemaVersion\":1.5}", "{\"schemaVersion\":0}",
                "{\"schemaVersion\":2147483648}", "{\"schemaVersion\":1,\"theme\":\"dark\"}",
                "{\"schemaVersion\":1,\"schemaVersion\":1}", new string(' ', 65537) };
            foreach (var document in documents)
            {
                File.WriteAllText(box.Paths.SettingsFile, document);
                var read = box.Settings().Load();
                Equal(false, read.CanSave);
                Equal(true, read.Diagnostics.Count > 0);
                Equal(document, File.ReadAllText(box.Paths.SettingsFile));
            }
        });
        test("Future settings schema never loads backup or downgrades", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new()); store.Save(new());
            const string future = "{\"schemaVersion\":2,\"futureSetting\":true}";
            File.WriteAllText(box.Paths.SettingsFile, future);
            var read = store.Load();
            Equal(false, read.CanSave); Equal(false, read.FromBackup);
            Equal(true, read.Diagnostics.Any(d => d.Event == "storage.schema.future"));
            Throws<PersistenceException>(store.RestoreBackup);
            Throws<PersistenceException>(() => store.Save(new()));
            Equal(future, File.ReadAllText(box.Paths.SettingsFile));
            Equal(0, Directory.GetFiles(box.Paths.DataDirectory, "*.preserved-*").Length);
        });
        test("Future or invalid backup is preserved on save", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new());
            const string future = "{\"schemaVersion\":5}";
            File.WriteAllText(store.BackupFile, future);
            Equal(false, store.Load().CanSave);
            Throws<PersistenceException>(() => store.Save(new()));
            Equal(future, File.ReadAllText(store.BackupFile));
            File.WriteAllText(store.BackupFile, "corrupt backup");
            var loaded = store.Load();
            Equal(new ApplicationSettings(), loaded.Value); Equal(false, loaded.CanSave);
            Equal(true, loaded.Diagnostics.Any(d => d.Event == "storage.read.failed"));
            Throws<PersistenceException>(() => store.Save(new()));
            Equal("corrupt backup", File.ReadAllText(store.BackupFile));
        });
        test("Corrupt primary uses backup read-only; explicit restore preserves bytes", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new()); store.Save(new());
            var corrupt = new byte[] { 0xff, 0x00, 0x7b };
            File.WriteAllBytes(box.Paths.SettingsFile, corrupt);
            var read = store.Load(); Equal(true, read.FromBackup); Equal(false, read.CanSave);
            Throws<PersistenceException>(() => store.Save(read.Value));
            store.RestoreBackup();
            var preserved = Directory.GetFiles(box.Paths.DataDirectory, "settings.json.preserved-*").Single();
            Equal(true, corrupt.SequenceEqual(File.ReadAllBytes(preserved)));
            Equal(true, File.Exists(store.BackupFile));
            Equal(true, store.Load().CanSave); store.Save(new());
        });
        test("Missing primary with backup requires explicit recovery", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new()); store.Save(new());
            File.Delete(box.Paths.SettingsFile);
            Equal(true, store.Load().FromBackup);
            Throws<PersistenceException>(() => store.Save(new()));
            store.RestoreBackup(); Equal(true, store.Load().CanSave);
        });
        test("Invalid backup is not adopted or overwritten during recovery", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings();
            File.WriteAllText(store.BackupFile, "invalid backup");
            Equal(false, store.Load().CanSave);
            Throws<PersistenceException>(store.RestoreBackup);
            Throws<PersistenceException>(() => store.Save(new()));
            Equal("invalid backup", File.ReadAllText(store.BackupFile));
            Equal(false, File.Exists(box.Paths.SettingsFile));
        });
        test("Atomic interruption before commit keeps primary and last valid backup", () =>
        {
            using var box = new Sandbox();
            var store = box.State(); store.Save(Snapshot(29)); store.Save(Snapshot(28));
            var primary = File.ReadAllText(box.Paths.StateFile); var backup = File.ReadAllText(store.BackupFile);
            var commits = 0;
            var failing = box.State(new BoundaryCommitter((temp, destination, _) =>
            {
                commits++;
                Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(temp));
                using var json = JsonDocument.Parse(File.ReadAllText(temp));
                Equal(27 * TimeSpan.TicksPerSecond, json.RootElement.GetProperty("remainingTicks").GetInt64());
                throw new IOException("Injected interruption before atomic replacement");
            }));
            Throws<PersistenceException>(() => failing.Save(Snapshot(27)));
            Equal(1, commits); Equal(primary, File.ReadAllText(box.Paths.StateFile));
            Equal(backup, File.ReadAllText(store.BackupFile));
            Equal(0, Directory.GetFiles(box.Paths.DataDirectory, "*.tmp").Length);
        });
        test("Abrupt process exit leaves flushed orphan ignored by next startup", () =>
        {
            using var box = new Sandbox();
            var store = box.State(); store.Save(Snapshot(29)); store.Save(Snapshot(28));
            var primary = File.ReadAllText(box.Paths.StateFile); var backup = File.ReadAllText(store.BackupFile);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--interrupt-state-save"); start.ArgumentList.Add(box.Root);
            using var child = Process.Start(start) ?? throw new Exception("Cannot launch interruption test child");
            if (!child.WaitForExit(30000)) { child.Kill(); throw new Exception("Interruption child timed out"); }
            Equal(73, child.ExitCode);
            Equal(primary, File.ReadAllText(box.Paths.StateFile)); Equal(backup, File.ReadAllText(store.BackupFile));
            var orphan = Directory.GetFiles(box.Paths.DataDirectory, "*.tmp").Single();
            using var json = JsonDocument.Parse(File.ReadAllText(orphan));
            Equal(27 * TimeSpan.TicksPerSecond, json.RootElement.GetProperty("remainingTicks").GetInt64());
            Equal(Snapshot(28), box.State().Load().Value);
        });
        test("Failure reported after commit still leaves a consistent primary and backup", () =>
        {
            using var box = new Sandbox();
            var store = box.State(); store.Save(Snapshot(29));
            var failing = box.State(new BoundaryCommitter((temp, target, backup) =>
            {
                new AtomicFileCommitter().Commit(temp, target, backup);
                throw new IOException("Injected interruption after commit");
            }));
            Throws<PersistenceException>(() => failing.Save(Snapshot(28)));
            Equal(Snapshot(28), box.State().Load().Value);
            File.Delete(box.Paths.StateFile); store.RestoreBackup(); Equal(Snapshot(29), store.Load().Value);
        });
        test("Interrupted first save does not create a primary or adopt an orphan", () =>
        {
            using var box = new Sandbox();
            var store = box.Settings(new BoundaryCommitter((_, _, _) => throw new IOException("Interrupted first save")));
            Throws<PersistenceException>(() => store.Save(new()));
            Equal(false, File.Exists(box.Paths.SettingsFile));
            File.WriteAllText(box.Paths.SettingsFile + ".orphan.tmp", "{\"schemaVersion\":1}");
            Equal(true, box.Settings().Load().CanSave); Equal(false, File.Exists(box.Paths.SettingsFile));
        });
        test("Data path blocked by file fails clearly with no fallback or overwrite", () =>
        {
            using var box = new Sandbox(initialize: false);
            File.WriteAllText(box.Paths.DataDirectory, "existing data");
            var error = Throws<PersistenceException>(() => box.Paths.Initialize(box.Diagnostics.Add));
            Equal(true, error.Message.Contains("Data 目录不可写", StringComparison.Ordinal));
            Equal("existing data", File.ReadAllText(box.Paths.DataDirectory));
        });
        test("Actual read-only Data directory rejects saves without changing data", () =>
        {
            if (OperatingSystem.IsWindows()) throw new TestSkipped("Unix permissions test; Windows ACL validation is separate");
            using var box = new Sandbox();
            var store = box.Settings(); store.Save(new()); var primary = File.ReadAllText(box.Paths.SettingsFile);
            var mode = File.GetUnixFileMode(box.Paths.DataDirectory);
            try
            {
                File.SetUnixFileMode(box.Paths.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                Throws<PersistenceException>(box.Paths.EnsureDataWritable);
                Throws<PersistenceException>(() => store.Save(new()));
                Equal(primary, File.ReadAllText(box.Paths.SettingsFile));
            }
            finally { File.SetUnixFileMode(box.Paths.DataDirectory, mode); }
        });
        test("Logs path blocked by file warns but timer still completes", () =>
        {
            using var box = new Sandbox(initialize: false);
            File.WriteAllText(box.Paths.LogsDirectory, "existing logs");
            box.Paths.Initialize(box.Diagnostics.Add);
            Equal(true, box.Diagnostics.Any(d => d.Event == "logs.unwritable"));
            var log = new LogService(box.Paths); var count = 0; log.Failed += _ => count++;
            log.Failed += _ => throw new Exception("Broken diagnostic sink");
            var clock = new FakeClock(); var timer = new TestTimer(clock);
            timer.Completed += () => log.Write(DiagnosticLevel.Information, "timer.completed", "完成");
            timer.Start(); clock.Advance(30); timer.Tick();
            Equal(TimerState.Completed, timer.State); Equal(1, count); Equal(true, log.LastFailure != null);
            Equal("existing logs", File.ReadAllText(box.Paths.LogsDirectory));
        });
        test("Actual read-only Logs directory warns without blocking initialization", () =>
        {
            if (OperatingSystem.IsWindows()) throw new TestSkipped("Unix permissions test; Windows ACL validation is separate");
            using var box = new Sandbox(); var mode = File.GetUnixFileMode(box.Paths.LogsDirectory);
            try
            {
                File.SetUnixFileMode(box.Paths.LogsDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                box.Paths.Initialize(box.Diagnostics.Add);
                Equal(true, box.Diagnostics.Any(d => d.Event == "logs.unwritable"));
                var log = new LogService(box.Paths); log.Write(DiagnosticLevel.Warning, "test", "warning");
                Equal("logs.write.failed", log.LastFailure!.Event);
            }
            finally { File.SetUnixFileMode(box.Paths.LogsDirectory, mode); }
        });
        test("Broken storage diagnostic callback cannot break recovery or timer", () =>
        {
            using var box = new Sandbox(); File.WriteAllText(box.Paths.StateFile, "broken");
            Action<PersistenceDiagnostic> broken = _ => throw new Exception("Broken sink");
            var store = new TimerStateStore(box.Paths, broken); Equal(false, store.Load().CanSave);
            var clock = new FakeClock(); var timer = new TestTimer(clock);
            using var session = new TimerPersistenceSession(timer, store, broken);
            timer.Start(); clock.Advance(30); timer.Tick(); Equal(TimerState.Completed, timer.State);
        });
        test("Running timer restarts paused with exact fractional remaining and no downtime", () =>
        {
            using var box = new Sandbox(); var clock = new FakeClock(); var timer = new TestTimer(clock);
            using (var session = new TimerPersistenceSession(timer, box.State(), box.Diagnostics.Add))
            {
                timer.Start(); clock.Advance(7.125); // Dispose is the final exit checkpoint.
            }
            var restartedClock = new FakeClock(); restartedClock.Advance(86400);
            var restarted = new TestTimer(restartedClock); var completed = 0; restarted.Completed += () => completed++;
            using var restored = new TimerPersistenceSession(restarted, box.State(), box.Diagnostics.Add);
            Equal(TimerState.Paused, restarted.State); Equal(22.875, restarted.Remaining.TotalSeconds);
            restartedClock.Advance(3600); restarted.Tick(); Equal(22.875, restarted.Remaining.TotalSeconds);
            Equal(TimerState.Paused, box.State().Load().Value.State); Equal(0, completed);
            restarted.Resume(); restartedClock.Advance(0.125); restarted.Tick(); Equal(22.75, restarted.Remaining.TotalSeconds);
        });
        test("Ordinary 250ms ticks never commit; transitions completion and exit do", () =>
        {
            using var box = new Sandbox(); var commits = 0;
            var store = box.State(new BoundaryCommitter((temp, target, backup) =>
            {
                commits++; new AtomicFileCommitter().Commit(temp, target, backup);
            }));
            var clock = new FakeClock(); var timer = new TestTimer(clock);
            var session = new TimerPersistenceSession(timer, store, box.Diagnostics.Add);
            Equal(0, commits); timer.Start(); Equal(1, commits);
            for (var i = 0; i < 100; i++) { clock.Advance(0.25); timer.Tick(); }
            Equal(1, commits); timer.Pause(); Equal(2, commits); timer.Resume(); Equal(3, commits);
            clock.Advance(5); timer.Tick(); Equal(4, commits); Equal(TimerState.Completed, store.Load().Value.State);
            session.Dispose(); Equal(5, commits); session.Dispose(); Equal(5, commits);
            timer.Start(); Equal(5, commits);
        });
        test("Ready paused completed and expired snapshots restore without completion replay", () =>
        {
            var states = new[] { Snapshot(30, TimerState.Ready), Snapshot(12), Snapshot(0, TimerState.Completed), Snapshot(0, TimerState.Running) };
            foreach (var snapshot in states)
            {
                using var box = new Sandbox(); box.State().Save(snapshot);
                var timer = new TestTimer(new FakeClock()); var completions = 0; timer.Completed += () => completions++;
                using var session = new TimerPersistenceSession(timer, box.State(), box.Diagnostics.Add);
                Equal(snapshot.State == TimerState.Running ? TimerState.Completed : snapshot.State, timer.State);
                Equal(snapshot.RemainingTicks, timer.Remaining.Ticks); Equal(0, completions);
            }
        });
        test("Invalid timer parameters and incomplete state JSON cannot be adopted", () =>
        {
            using var box = new Sandbox(); var store = box.State(); store.Save(Snapshot(12));
            var original = File.ReadAllText(box.Paths.StateFile);
            var invalid = new[] { Snapshot(-1), Snapshot(31), Snapshot(12, TimerState.Ready),
                Snapshot(1, TimerState.Completed), Snapshot(0), Snapshot(12, (TimerState)99),
                Snapshot(12) with { DurationTicks = TimeSpan.TicksPerSecond }, Snapshot(12) with { SchemaVersion = 2 } };
            foreach (var snapshot in invalid) Throws<PersistenceException>(() => store.Save(snapshot));
            Equal(original, File.ReadAllText(box.Paths.StateFile));
            File.WriteAllText(box.Paths.StateFile, "{\"schemaVersion\":1,\"state\":\"Paused\"}");
            Equal(false, store.Load().CanSave);
            File.WriteAllText(box.Paths.StateFile, original.Replace("\"Paused\"", "2", StringComparison.Ordinal));
            Equal(false, store.Load().CanSave);
        });
        test("Corrupt state uses valid backup paused without modifying corrupt primary", () =>
        {
            using var box = new Sandbox(); var store = box.State();
            store.Save(Snapshot(20, TimerState.Running)); store.Save(Snapshot(19));
            File.WriteAllText(box.Paths.StateFile, "broken state");
            var timer = new TestTimer(new FakeClock());
            using var session = new TimerPersistenceSession(timer, store, box.Diagnostics.Add);
            Equal(TimerState.Paused, timer.State); Equal(20d, timer.Remaining.TotalSeconds);
            timer.Resume(); session.Checkpoint(); Equal("broken state", File.ReadAllText(box.Paths.StateFile));
        });
        test("Future state remains untouched while timer operates in memory", () =>
        {
            using var box = new Sandbox(); const string future = "{\"schemaVersion\":9}";
            File.WriteAllText(box.Paths.StateFile, future);
            var clock = new FakeClock(); var timer = new TestTimer(clock);
            using var session = new TimerPersistenceSession(timer, box.State(), box.Diagnostics.Add);
            Equal(TimerState.Ready, timer.State); timer.Start(); clock.Advance(30); timer.Tick();
            Equal(TimerState.Completed, timer.State); Equal(future, File.ReadAllText(box.Paths.StateFile));
        });
        test("State save failure does not abort completion or notification callback", () =>
        {
            using var box = new Sandbox(); var completions = 0;
            var store = box.State(new BoundaryCommitter((_, _, _) => throw new IOException("Disk full")));
            var clock = new FakeClock(); var timer = new TestTimer(clock);
            using var session = new TimerPersistenceSession(timer, store, box.Diagnostics.Add);
            timer.Completed += () => completions++; timer.Start(); clock.Advance(30); timer.Tick();
            Equal(1, completions); Equal(TimerState.Completed, timer.State);
            Equal(true, box.Diagnostics.Any(d => d.Event == "timer.checkpoint.failed"));
        });
        test("Log entries contain UTC level event exception and redact named credentials", () =>
        {
            using var box = new Sandbox(); var log = new LogService(box.Paths, utcNow: () => new(2026, 11, 11, 8, 0, 0, TimeSpan.FromHours(8)));
            log.Write(DiagnosticLevel.Error, "test.failure", "中文 api_key=abc123 password=topsecret Authorization: Bearer abc.def",
                new IOException("secret=hidden-token"));
            using var json = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(box.Paths.LogsDirectory).Single()));
            Equal("2026-11-11T00:00:00.0000000+00:00", json.RootElement.GetProperty("timestampUtc").GetString());
            Equal("Error", json.RootElement.GetProperty("level").GetString());
            Equal("test.failure", json.RootElement.GetProperty("event").GetString());
            var message = json.RootElement.GetProperty("message").GetString()!;
            Equal(true, message.Contains("中文"));
            foreach (var secret in new[] { "abc123", "topsecret", "abc.def", "hidden-token" })
                Equal(false, json.RootElement.ToString().Contains(secret, StringComparison.Ordinal));
            Equal(true, json.RootElement.GetProperty("exception").GetString()!.Contains("IOException"));
            log.Write(DiagnosticLevel.Warning, "test.redaction", "\"password\":\"multi word value\", 'apiKey':'quoted-value' Basic dXNlcjpwYXNz");
            var all = File.ReadAllText(Directory.GetFiles(box.Paths.LogsDirectory).Single());
            foreach (var secret in new[] { "multi word value", "quoted-value", "dXNlcjpwYXNz" })
                Equal(false, all.Contains(secret, StringComparison.Ordinal));
        });
        test("Logs rotate on UTC day and retain only the last 14 days", () =>
        {
            using var box = new Sandbox(); var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
            var log = new LogService(box.Paths, utcNow: () => now);
            for (var i = 0; i < 15; i++) { log.Write(DiagnosticLevel.Information, "day", "每日轮转"); now = now.AddDays(1); }
            Equal(14, Directory.GetFiles(box.Paths.LogsDirectory).Length);
            Equal(false, File.Exists(box.Paths.LogFile(new(2026, 10, 28), 0)));
            Equal(true, File.Exists(box.Paths.LogFile(new(2026, 10, 29), 0)));
            Equal(null, log.LastFailure);
        });
        test("Logs rotate under the byte cap and enforce total capacity", () =>
        {
            using var box = new Sandbox(); var limits = new LogLimits { MaximumFileBytes = 1024, MaximumTotalBytes = 3072 };
            var log = new LogService(box.Paths, limits, () => new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));
            for (var i = 0; i < 50; i++) log.Write(DiagnosticLevel.Information, "capacity", $"{i}: " + new string('中', 40));
            var files = Directory.GetFiles(box.Paths.LogsDirectory).Select(p => new FileInfo(p)).ToArray();
            Equal(true, files.Length > 1); Equal(true, files.All(f => f.Length <= limits.MaximumFileBytes));
            Equal(true, files.Sum(f => f.Length) <= limits.MaximumTotalBytes); Equal(null, log.LastFailure);
            foreach (var file in files)
                foreach (var line in File.ReadLines(file.FullName)) using (JsonDocument.Parse(line)) { }
        });
        test("Oversized log entries stay valid JSON within the file cap", () =>
        {
            using var box = new Sandbox(); var limits = new LogLimits { MaximumFileBytes = 512, MaximumTotalBytes = 1024 };
            var log = new LogService(box.Paths, limits);
            log.Write(DiagnosticLevel.Error, "large", new string('中', 10000), new IOException(new string('x', 20000)));
            var file = Directory.GetFiles(box.Paths.LogsDirectory).Single();
            Equal(true, new FileInfo(file).Length <= 512);
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Equal("logs.entry.truncated", json.RootElement.GetProperty("event").GetString()); Equal(null, log.LastFailure);
        });
        test("Cleanup preserves in-use logs and unrelated files with a warning", () =>
        {
            using var box = new Sandbox(); var oldFile = box.Paths.LogFile(new(2026, 10, 1), 0);
            File.WriteAllText(oldFile, "in-use log");
            var unrelated = Path.Combine(box.Paths.LogsDirectory, "user-notes.log"); File.WriteAllText(unrelated, "user content");
            using var held = new FileStream(oldFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var log = new LogService(box.Paths, utcNow: () => new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));
            log.Write(DiagnosticLevel.Information, "cleanup", "不要删除正在使用的文件");
            Equal(true, File.Exists(oldFile)); Equal("user content", File.ReadAllText(unrelated));
            Equal("logs.cleanup.failed", log.LastFailure!.Event);
        });
        test("Default log limits are 5 MiB 50 MiB and 14 days", () =>
        {
            var limits = new LogLimits(); Equal(5L * 1024 * 1024, limits.MaximumFileBytes);
            Equal(50L * 1024 * 1024, limits.MaximumTotalBytes); Equal(14, limits.RetentionDays);
        });
    }

    public static void InterruptStateSave(string root)
    {
        var store = new TimerStateStore(new PortablePaths(root), _ => { },
            new BoundaryCommitter((_, _, _) => Environment.Exit(73)));
        store.Save(Snapshot(27));
    }

    private static TimerSnapshot Snapshot(double seconds, TimerState state = TimerState.Paused) => new()
    {
        State = state, DurationTicks = TestTimer.Duration.Ticks, RemainingTicks = TimeSpan.FromSeconds(seconds).Ticks
    };

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private sealed class Sandbox : IDisposable
    {
        public Sandbox(string name = "Portable tests", bool initialize = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"pomodoro-{Guid.NewGuid():N}", name);
            Directory.CreateDirectory(Root); Paths = new(Root);
            if (initialize) Paths.Initialize(Diagnostics.Add);
        }
        public string Root { get; }
        public PortablePaths Paths { get; }
        public List<PersistenceDiagnostic> Diagnostics { get; } = [];
        public SettingsStore Settings(IAtomicFileCommitter? committer = null) => new(Paths, Diagnostics.Add, committer);
        public TimerStateStore State(IAtomicFileCommitter? committer = null) => new(Paths, Diagnostics.Add, committer);
        public void Dispose() => Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
    }

    private sealed class BoundaryCommitter(Action<string, string, string?> action) : IAtomicFileCommitter
    {
        public void Commit(string temporaryFile, string destinationFile, string? backupFile) =>
            action(temporaryFile, destinationFile, backupFile);
    }
}

internal sealed class TestSkipped(string message) : Exception(message);
