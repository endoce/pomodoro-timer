using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;
using PomodoroTimer.Spike.Infrastructure;
using PomodoroTimer.Spike.Services;

internal static class PortableRegressionTests
{
    private static readonly TimeSpan ContentionTimeout = TimeSpan.FromMilliseconds(80);

    public static void Register(Action<string, Action> test)
    {
        test("Exit checkpoint finishes and reports before cleanup ownership and WPF event", () =>
        {
            using var box = new Sandbox();
            var steps = new List<string>();
            var exit = new ExitCoordinator(d => steps.Add(d.Event));
            var timer = new TestTimer(new FakeClock());
            var session = new TimerPersistenceSession(timer, box.State(new Committer((temp, target, backup) =>
            {
                steps.Add("commit"); new AtomicFileCommitter().Commit(temp, target, backup);
            })), box.Report);
            exit.Run(session.Complete, [new("resources", () => steps.Add("resources"))],
                () => { Equal(CheckpointStatus.Saved, exit.FinalCheckpoint!.Status); steps.Add("key"); },
                () => { session.Dispose(); steps.Add("wpf.event"); });
            Equal("commit,exit.checkpoint.saved,resources,exit.step.completed,key,exit.step.completed,wpf.event,exit.step.completed",
                string.Join(',', steps));
            Equal(true, exit.Completed);
        });
        test("Exit duplicate and reentrant requests save dispose and raise event once", () =>
        {
            using var box = new Sandbox(); var saves = 0; var cleanup = 0; var releases = 0; var events = 0;
            var exit = new ExitCoordinator(box.Report);
            var session = new TimerPersistenceSession(new TestTimer(new FakeClock()), box.State(new Committer((t, f, b) =>
            {
                saves++; exit.Run(() => throw new Exception("Reentered finalize"), [], () => releases++, () => events++);
                new AtomicFileCommitter().Commit(t, f, b);
            })), box.Report);
            void Run() => exit.Run(session.Complete, [new("dispose", () => { cleanup++; session.Dispose(); })],
                () => releases++, () => events++);
            Run(); Run(); Run(); session.Dispose(); session.Checkpoint();
            Equal(1, saves); Equal(1, cleanup); Equal(1, releases); Equal(1, events);
            Equal(CheckpointStatus.Saved, session.Complete().Status);
        });
        test("Exit blocked successful save retains ownership until real commit completes", () => ExitBlockedSave(fail: false));
        test("Exit blocked failed save retains ownership and still cleans up deterministically", () => ExitBlockedSave(fail: true));
        test("Exit finalize exception and cleanup failures are isolated and diagnosed", () =>
        {
            using var box = new Sandbox(); var steps = new List<string>(); var exit = new ExitCoordinator(box.Report);
            exit.Run(() => throw new IOException("Artificial final failure"),
                [new("broken", () => throw new IOException("Artificial cleanup failure")), new("next", () => steps.Add("next"))],
                () => { steps.Add("key"); throw new IOException("Artificial release failure"); }, () => steps.Add("event"));
            Equal(CheckpointStatus.Failed, exit.FinalCheckpoint!.Status);
            Equal("next,key,event", string.Join(',', steps)); Equal(true, exit.Completed);
            Equal(2, box.Diagnostics.Count(d => d.Event == "exit.step.failed"));
        });
        test("Exit immediate restart at ownership release reads final fractional snapshot", () =>
        {
            using var box = new Sandbox(); var clock = new FakeClock(); var timer = new TestTimer(clock);
            using var session = new TimerPersistenceSession(timer, box.State(), box.Report);
            timer.Start(); clock.Advance(7.125);
            var exit = new ExitCoordinator(box.Report);
            exit.Run(session.Complete, [], () =>
            {
                var restored = new TestTimer(new FakeClock());
                using var restarted = new TimerPersistenceSession(restored, box.State(), box.Report);
                Equal(TimerState.Paused, restored.State); Equal(22.875, restored.Remaining.TotalSeconds);
                Equal(CheckpointStatus.Saved, exit.FinalCheckpoint!.Status);
            }, () => { });
        });
        test("Exit disabled persistence is explicit and does not overwrite future state", () =>
        {
            using var box = new Sandbox(); const string future = "{\"schemaVersion\":9}";
            File.WriteAllText(box.Paths.StateFile, future);
            using var session = new TimerPersistenceSession(new TestTimer(new FakeClock()), box.State(), box.Report);
            var exit = new ExitCoordinator(box.Report); var released = false;
            exit.Run(session.Complete, [], () => released = true, () => { });
            Equal(CheckpointStatus.Disabled, exit.FinalCheckpoint!.Status); Equal(true, released);
            Equal(future, File.ReadAllText(box.Paths.StateFile));
        });
        test("Exit InProgress never releases ownership and permits a finished retry", () =>
        {
            using var box = new Sandbox(); var exit = new ExitCoordinator(box.Report); var released = 0;
            exit.Run(() => new(CheckpointStatus.InProgress), [], () => released++, () => { });
            Equal(0, released); Equal(false, exit.Completed);
            exit.Run(() => new(CheckpointStatus.NotRequired), [], () => released++, () => { });
            Equal(1, released); Equal(true, exit.Completed);
        });
        test("Session reentrant completion during transition cannot misreport a nested failure", () =>
        {
            using var box = new Sandbox(); TimerPersistenceSession? session = null; var commits = 0;
            session = new(new TestTimer(new FakeClock()), box.State(new Committer((t, f, b) =>
            {
                commits++; Equal(CheckpointStatus.InProgress, session!.Complete().Status);
                new AtomicFileCommitter().Commit(t, f, b);
            })), box.Report);
            Equal(CheckpointStatus.Saved, session.Checkpoint().Status);
            Equal(CheckpointStatus.Saved, session.Complete().Status); session.Dispose(); Equal(2, commits);
        });

        test("Thread stores simultaneous Save serialize normalized aliases and preserve valid backup", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29));
            using var block = new BlockingCommitter();
            using var owner = new Worker(() => box.State(block).Save(Snapshot(28)));
            block.WaitReady();
            var alias = new AtomicJsonStore<TimerSnapshot>(Path.Combine(box.Paths.DataDirectory, ".", "state.json"),
                () => Snapshot(30, TimerState.Ready), TestTimer.ValidateSnapshot, box.Report, null, ContentionTimeout);
            try
            {
                var error = Throws<PersistenceException>(() => alias.Save(Snapshot(27)));
                Equal(true, error.ToString().Contains("等待超时", StringComparison.Ordinal));
                Equal(Snapshot(29), ReadSnapshot(box.Paths.StateFile)); Equal(false, File.Exists(alias.BackupFile));
            }
            finally { block.Release.Set(); owner.Join(); }
            alias.Save(Snapshot(27));
            Equal(Snapshot(27), box.State().Load().Value); Equal(Snapshot(28), ReadSnapshot(alias.BackupFile));
        });
        test("Thread Save versus Restore cannot interleave validation and replacement", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29)); box.State().Save(Snapshot(28));
            using var block = new BlockingCommitter(); using var owner = new Worker(() => box.State(block).Save(Snapshot(27)));
            block.WaitReady(); var contender = box.State(timeout: ContentionTimeout);
            try
            {
                Throws<PersistenceException>(contender.RestoreBackup);
                Equal(Snapshot(28), ReadSnapshot(box.Paths.StateFile)); Equal(Snapshot(29), ReadSnapshot(contender.BackupFile));
                Equal(0, Directory.GetFiles(box.Paths.DataDirectory, "*.preserved-*").Length);
            }
            finally { block.Release.Set(); owner.Join(); }
            contender.RestoreBackup(); Equal(Snapshot(28), contender.Load().Value);
            Equal(Snapshot(28), ReadSnapshot(contender.BackupFile));
            Equal(Snapshot(27), ReadSnapshot(Directory.GetFiles(box.Paths.DataDirectory, "*.preserved-*").Single()));
        });
        test("Thread Restore versus Save protects damaged primary and last valid backup", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29)); box.State().Save(Snapshot(28));
            File.WriteAllText(box.Paths.StateFile, "artificial corrupt primary");
            using var block = new BlockingCommitter(); using var owner = new Worker(box.State(block).RestoreBackup);
            block.WaitReady(); var contender = box.State(timeout: ContentionTimeout);
            try
            {
                Throws<PersistenceException>(() => contender.Save(Snapshot(27)));
                Equal("artificial corrupt primary", File.ReadAllText(box.Paths.StateFile));
                Equal(Snapshot(29), ReadSnapshot(contender.BackupFile));
            }
            finally { block.Release.Set(); owner.Join(); }
            Equal(Snapshot(29), contender.Load().Value);
            Equal("artificial corrupt primary", File.ReadAllText(Directory.GetFiles(box.Paths.DataDirectory, "*.preserved-*").Single()));
            contender.Save(Snapshot(27)); Equal(Snapshot(29), ReadSnapshot(contender.BackupFile));
        });
        test("Thread first creation contention cannot race unchecked File.Move", () =>
        {
            using var box = new Sandbox(); using var block = new BlockingCommitter();
            using var owner = new Worker(() => box.State(block).Save(Snapshot(28))); block.WaitReady();
            var contender = box.State(timeout: ContentionTimeout);
            try
            {
                Throws<PersistenceException>(() => contender.Save(Snapshot(27)));
                Equal(false, File.Exists(box.Paths.StateFile)); Equal(false, File.Exists(contender.BackupFile));
            }
            finally { block.Release.Set(); owner.Join(); }
            contender.Save(Snapshot(27)); Equal(Snapshot(27), contender.Load().Value);
            Equal(Snapshot(28), ReadSnapshot(contender.BackupFile));
        });
        test("Thread Load timeout is bounded read-only and disables saving", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29)); using var block = new BlockingCommitter();
            using var owner = new Worker(() => box.State(block).Save(Snapshot(28))); block.WaitReady();
            try
            {
                var elapsed = Stopwatch.StartNew(); var read = box.State(timeout: ContentionTimeout).Load();
                Equal(false, read.CanSave); Equal(false, read.FromBackup);
                Equal(true, read.Diagnostics.Any(d => d.Event == "storage.transaction.failed"));
                Equal(true, elapsed.Elapsed < TimeSpan.FromSeconds(3)); Equal(Snapshot(29), ReadSnapshot(box.Paths.StateFile));
            }
            finally { block.Release.Set(); owner.Join(); }
        });
        test("Thread independent paths do not share the transaction mutex", () =>
        {
            using var box = new Sandbox(); using var block = new BlockingCommitter();
            using var owner = new Worker(() => box.State(block).Save(Snapshot(28))); block.WaitReady();
            try
            {
                new SettingsStore(box.Paths, box.Report, transactionTimeout: ContentionTimeout).Save(new());
                Equal(true, File.Exists(box.Paths.SettingsFile)); Equal(false, File.Exists(box.Paths.StateFile));
            }
            finally { block.Release.Set(); owner.Join(); }
        });
        test("Thread same-path nested transaction is rejected without losing outer ownership", () =>
        {
            using var box = new Sandbox(); var store = box.State(new Committer((t, f, b) =>
            {
                Throws<PersistenceException>(() => box.State().Save(Snapshot(27)));
                Equal(false, File.Exists(f)); new AtomicFileCommitter().Commit(t, f, b);
            }));
            store.Save(Snapshot(28)); box.State().Save(Snapshot(26));
            Equal(Snapshot(26), box.State().Load().Value); Equal(Snapshot(28), ReadSnapshot(store.BackupFile));
        });
        test("Storage transaction timeout rejects unlimited or excessive waits", () =>
        {
            using var box = new Sandbox();
            foreach (var timeout in new[] { Timeout.InfiniteTimeSpan, TimeSpan.Zero, TimeSpan.FromSeconds(6) })
                Throws<ArgumentOutOfRangeException>(() => box.State(timeout: timeout));
        });
        test("Commit no-op cannot report Saved and final failure result is cached", () =>
        {
            using var box = new Sandbox(); var commits = 0;
            using var session = new TimerPersistenceSession(new TestTimer(new FakeClock()),
                box.State(new Committer((_, _, _) => commits++)), box.Report);
            var result = session.Complete(); Equal(CheckpointStatus.Failed, result.Status);
            Equal(true, ReferenceEquals(result, session.Complete())); session.Dispose(); Equal(1, commits);
            Equal(false, File.Exists(box.Paths.StateFile)); Equal(0, Directory.GetFiles(box.Paths.DataDirectory, "*.tmp").Length);
        });
        test("Commit pre-boundary failure releases transaction and preserves exact backup", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29)); box.State().Save(Snapshot(28));
            var primary = File.ReadAllBytes(box.Paths.StateFile); var backup = File.ReadAllBytes(box.State().BackupFile);
            Throws<PersistenceException>(() => box.State(new Committer((_, _, _) => throw new IOException("Artificial boundary failure"))).Save(Snapshot(27)));
            Equal(true, primary.SequenceEqual(File.ReadAllBytes(box.Paths.StateFile)));
            Equal(true, backup.SequenceEqual(File.ReadAllBytes(box.State().BackupFile)));
            box.State().Save(Snapshot(26)); Equal(Snapshot(26), box.State().Load().Value);
        });
        test("Commit post-boundary exception reports Failed despite valid new primary", () =>
        {
            using var box = new Sandbox(); box.State().Save(Snapshot(29));
            var timer = new TestTimer(new FakeClock());
            using var session = new TimerPersistenceSession(timer, box.State(new Committer((t, f, b) =>
            {
                new AtomicFileCommitter().Commit(t, f, b); throw new IOException("Artificial post-commit error");
            })), box.Report);
            Equal(CheckpointStatus.Failed, session.Complete().Status);
            Equal(Snapshot(29), box.State().Load().Value); Equal(Snapshot(29), ReadSnapshot(box.State().BackupFile));
        });
        test("Process cooperative Save contention is bounded and retry preserves backup", () => ProcessContention(kill: false, first: false));
        test("Process killed transaction owner leaves no permanent lock and orphan is ignored", () => ProcessContention(kill: true, first: false));
        test("Process first creation contention coordinates with parent Store", () => ProcessContention(kill: false, first: true));

        test("Redaction ordinary password apiKey and Bearer Basic stay supported", () =>
            Redaction("password=FAKE_PLAIN; apiKey: FAKE_KEY Authorization: Bearer FAKE.BEARER Basic RkFLRTpJTlZBTElE; note=visible",
                ["FAKE_PLAIN", "FAKE_KEY", "FAKE.BEARER", "RkFLRTpJTlZBTElE"], "note=visible"));
        test("Redaction JSON escaped double quote removes the entire credential suffix", () =>
            Redaction("""{"password":"FAKE_HEAD\"FAKE_TAIL","note":"visible"}""", ["FAKE_HEAD", "FAKE_TAIL"], "\"note\":\"visible\""));
        test("Redaction odd and even backslash runs preserve quoted value boundaries", () =>
        {
            for (var count = 1; count <= 12; count++)
            {
                var value = "\"apiKey\":\"FAKE_SLASH" + new string('\\', count) + "\"";
                value += count % 2 == 0 ? ", note=visible" : "FAKE_SUFFIX\", note=visible";
                Redaction(value, ["FAKE_SLASH", "FAKE_SUFFIX"], "note=visible");
            }
        });
        test("Redaction mixed single double and escaped single quotes", () =>
            Redaction("""password='FAKE_SINGLE\'FAKE_TAIL"inside'; apiKey="FAKE_DOUBLE'inside"; note=visible""",
                ["FAKE_SINGLE", "FAKE_TAIL", "FAKE_DOUBLE", "inside"], "note=visible"));
        test("Redaction multiple fields and credential-like embedded fields cannot leak", () =>
            Redaction("""{"password":"FAKE_ONE apiKey=FAKE_EMBEDDED","access_token":"FAKE_TWO","refresh-token":'FAKE_THREE',"note":"visible"}""",
                ["FAKE_ONE", "FAKE_EMBEDDED", "FAKE_TWO", "FAKE_THREE"], "visible"));
        test("Redaction thrown exception inner message stack and event use the same scanner", () =>
        {
            using var box = new Sandbox(); var log = new LogService(box.Paths);
            try
            {
                throw new IOException("""password="FAKE_EXCEPTION\"FAKE_SUFFIX", note=visible""",
                    new Exception("secret='FAKE_INNER'"));
            }
            catch (IOException error) { log.Write(DiagnosticLevel.Error, "apiKey=FAKE_EVENT", "note=visible", error); }
            var entries = Entries(box); Equal(null, log.LastFailure);
            foreach (var secret in new[] { "FAKE_EXCEPTION", "FAKE_SUFFIX", "FAKE_INNER", "FAKE_EVENT" })
                Equal(false, entries.Contains(secret, StringComparison.Ordinal));
            Equal(true, entries.Contains("IOException", StringComparison.Ordinal)); Equal(true, entries.Contains("visible", StringComparison.Ordinal));
        });
        test("Redaction damaged unterminated dangling and illegal quoted suffix fail closed", () =>
        {
            foreach (var value in new[] { "password=\"FAKE_UNCLOSED", "apiKey='FAKE_UNCLOSED\\", "password=\"FAKE_HEAD\"FAKE_TAIL",
                "passwd='FAKE_HEAD\"FAKE_TAIL", "secret=\"FAKE_HEAD\\\"FAKE_TAIL" })
                Redaction("note=visible; " + value, ["FAKE_UNCLOSED", "FAKE_HEAD", "FAKE_TAIL"], "note=visible");
            Redaction("password=", [], "[REDACTED]");
        });
        test("Redaction non-sensitive JSON remains intact", () =>
            Redaction("""{"note":"ordinary \\"text\\"","count":42}""", [], "\"count\":42"));
        test("Redaction long escaped malformed value has bounded work and never leaks suffix", () =>
            Redaction("note=visible; password=\"FAKE_LONG" + new string('\\', 40000) + "\\\"FAKE_SUFFIX",
                ["FAKE_LONG", "FAKE_SUFFIX"], "note=visible"));
        test("Redaction exception serialization failure never writes original raw credentials", () =>
        {
            using var box = new Sandbox(); var log = new LogService(box.Paths);
            log.Write(DiagnosticLevel.Information, "before", "note=visible"); var before = Entries(box);
            log.Write(DiagnosticLevel.Error, "password=FAKE_EVENT", "apiKey=FAKE_RAW_MESSAGE", new BrokenException());
            Equal("logs.write.failed", log.LastFailure!.Event); Equal(before, Entries(box));
            Equal(false, Entries(box).Contains("FAKE_", StringComparison.Ordinal));
        });
    }

    private static void ExitBlockedSave(bool fail)
    {
        using var box = new Sandbox(); box.State().Save(Snapshot(29));
        using var block = new BlockingCommitter(fail); var exit = new ExitCoordinator(box.Report);
        var owned = 1; var cleanup = 0; var events = 0;
        using var owner = new Worker(() =>
        {
            using var session = new TimerPersistenceSession(new TestTimer(new FakeClock()), box.State(block), box.Report);
            exit.Run(session.Complete, [new("resources", () => { Equal(1, Volatile.Read(ref owned)); cleanup++; })],
                () => { Equal(fail ? CheckpointStatus.Failed : CheckpointStatus.Saved, exit.FinalCheckpoint!.Status); Interlocked.Exchange(ref owned, 0); },
                () => events++);
        });
        block.WaitReady();
        try
        {
            Equal(1, Volatile.Read(ref owned)); Equal(null, exit.FinalCheckpoint);
            Throws<PersistenceException>(() => box.State(timeout: ContentionTimeout).Save(Snapshot(26)));
            Equal(1, Volatile.Read(ref owned)); Equal(0, cleanup); Equal(0, events);
        }
        finally { block.Release.Set(); owner.Join(); }
        Equal(0, owned); Equal(1, cleanup); Equal(1, events); Equal(true, exit.Completed);
        Equal(true, box.Diagnostics.Any(d => d.Event == (fail ? "exit.checkpoint.failed" : "exit.checkpoint.saved")));
    }

    public static void HoldStateSave(string root)
    {
        var paths = new PortablePaths(root);
        new TimerStateStore(paths, _ => { }, new Committer((temp, target, backup) =>
        {
            File.WriteAllText(Path.Combine(root, "owner.ready"), "flushed; transaction owned");
            if (!SpinWait.SpinUntil(() => File.Exists(Path.Combine(root, "owner.release")), TimeSpan.FromSeconds(15)))
                throw new IOException("Child release barrier timed out");
            new AtomicFileCommitter().Commit(temp, target, backup);
        })).Save(Snapshot(27));
    }

    private static void ProcessContention(bool kill, bool first)
    {
        using var box = new Sandbox();
        if (!first) { box.State().Save(Snapshot(29)); box.State().Save(Snapshot(28)); }
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--hold-state-save"); start.ArgumentList.Add(box.Root);
        using var child = Process.Start(start) ?? throw new Exception("Cannot start transaction child");
        try
        {
            Equal(true, SpinWait.SpinUntil(() => File.Exists(Path.Combine(box.Root, "owner.ready")) || child.HasExited, TimeSpan.FromSeconds(10)));
            Equal(false, child.HasExited); Equal(true, File.Exists(Path.Combine(box.Root, "owner.ready")));
            var contender = box.State(timeout: ContentionTimeout);
            Throws<PersistenceException>(() => contender.Save(Snapshot(26)));
            Throws<PersistenceException>(contender.RestoreBackup); Equal(false, contender.Load().CanSave);
            if (first) Equal(false, File.Exists(box.Paths.StateFile));
            else { Equal(Snapshot(28), ReadSnapshot(box.Paths.StateFile)); Equal(Snapshot(29), ReadSnapshot(contender.BackupFile)); }
            if (kill) child.Kill(); else File.WriteAllText(Path.Combine(box.Root, "owner.release"), "release");
            Equal(true, child.WaitForExit(10000));
            if (!kill) Equal(0, child.ExitCode); else Equal(true, child.ExitCode != 0);
            Equal(first ? Snapshot(27) : kill ? Snapshot(28) : Snapshot(27), box.State().Load().Value);
            Equal(kill ? 1 : 0, Directory.GetFiles(box.Paths.DataDirectory, "*.tmp").Length);
            contender.Save(Snapshot(26)); Equal(Snapshot(26), contender.Load().Value);
            Equal(kill ? Snapshot(28) : Snapshot(27), ReadSnapshot(contender.BackupFile));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); child.WaitForExit(10000); }
        }
    }

    private static void Redaction(string input, string[] forbidden, string preserved)
    {
        using var box = new Sandbox(); var log = new LogService(box.Paths); var elapsed = Stopwatch.StartNew();
        log.Write(DiagnosticLevel.Information, "redaction.regression", input);
        Equal(null, log.LastFailure); Equal(true, elapsed.Elapsed < TimeSpan.FromSeconds(3));
        var text = Entries(box);
        foreach (var secret in forbidden) Equal(false, text.Contains(secret, StringComparison.Ordinal));
        Equal(true, text.Contains(preserved, StringComparison.Ordinal));
        if (forbidden.Length > 0) Equal(true, text.Contains("[REDACTED]", StringComparison.Ordinal));
    }

    private static string Entries(Sandbox box) => string.Join('\n', Directory.GetFiles(box.Paths.LogsDirectory).SelectMany(File.ReadLines)
        .Select(line => { using var json = JsonDocument.Parse(line); return string.Join('\n', json.RootElement.EnumerateObject().Select(p => p.Value.ToString())); }));
    private static TimerSnapshot ReadSnapshot(string path) => JsonSerializer.Deserialize<TimerSnapshot>(File.ReadAllText(path), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<TimerState>() }
    })!;
    private static TimerSnapshot Snapshot(double seconds, TimerState state = TimerState.Paused) => new()
    {
        State = state,
        DurationTicks = TestTimer.Duration.Ticks,
        RemainingTicks = TimeSpan.FromSeconds(seconds).Ticks
    };
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
    }
    private static T Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private sealed class Sandbox : IDisposable
    {
        public Sandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), $"pomodoro-regression-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root); Paths = new(Root); Paths.Initialize(Report);
        }
        public string Root { get; }
        public PortablePaths Paths { get; }
        public ConcurrentQueue<PersistenceDiagnostic> Diagnostics { get; } = new();
        public void Report(PersistenceDiagnostic diagnostic) => Diagnostics.Enqueue(diagnostic);
        public TimerStateStore State(IAtomicFileCommitter? committer = null, TimeSpan? timeout = null) => new(Paths, Report, committer, timeout);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private sealed class Committer(Action<string, string, string?> run) : IAtomicFileCommitter
    {
        public void Commit(string temp, string target, string? backup) => run(temp, target, backup);
    }
    private sealed class BlockingCommitter(bool fail = false) : IAtomicFileCommitter, IDisposable
    {
        public ManualResetEventSlim Ready { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public void WaitReady() => Equal(true, Ready.Wait(TimeSpan.FromSeconds(5)));
        public void Commit(string temp, string target, string? backup)
        {
            Ready.Set(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Thread release barrier timed out");
            if (fail) throw new IOException("Artificial blocked final save failure");
            new AtomicFileCommitter().Commit(temp, target, backup);
        }
        public void Dispose() { Release.Set(); Ready.Dispose(); Release.Dispose(); }
    }
    private sealed class Worker : IDisposable
    {
        private readonly Thread thread;
        private Exception? error;
        public Worker(Action run)
        {
            thread = new(() => { try { run(); } catch (Exception caught) { error = caught; } }) { IsBackground = true };
            thread.Start();
        }
        public void Join()
        {
            Equal(true, thread.Join(TimeSpan.FromSeconds(12)));
            if (error is not null) throw new Exception("Worker failed", error);
        }
        public void Dispose() => Join();
    }
    private sealed class BrokenException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("password=FAKE_SERIALIZATION_ERROR");
    }
}
