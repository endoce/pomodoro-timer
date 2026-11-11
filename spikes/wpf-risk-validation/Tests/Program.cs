using PomodoroTimer.Spike.Core.Models;
using PomodoroTimer.Spike.Core.Services;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
}

Test("Start does not subtract a second", () =>
{
    var timer = new TestTimer(new FakeClock()); timer.Start(); Equal(TimeSpan.FromSeconds(30), timer.Remaining);
});
Test("Duplicate Start does not restart", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); timer.Start(); clock.Advance(7); timer.Start(); Equal(23d, timer.Remaining.TotalSeconds);
});
Test("Delayed ticks do not accumulate drift", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); timer.Start(); clock.Advance(29.75); timer.Tick(); Equal(0.25, timer.Remaining.TotalSeconds);
});
Test("Pause excludes elapsed pause time", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); timer.Start(); clock.Advance(7.125); timer.Pause(); clock.Advance(3600); Equal(22.875, timer.Remaining.TotalSeconds);
});
Test("Resume does not subtract a second", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); timer.Start(); clock.Advance(1.5); timer.Pause(); timer.Resume(); Equal(28.5, timer.Remaining.TotalSeconds);
});
Test("Repeated pause resume retains subsecond time", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); timer.Start();
    for (var i=0; i<100; i++) { clock.Advance(0.01); timer.Pause(); clock.Advance(1); timer.Resume(); }
    Equal(TimeSpan.FromSeconds(29), timer.Remaining);
});
Test("Completion happens once even after repeated ticks", () =>
{
    var clock = new FakeClock(); var timer = new TestTimer(clock); var count=0; timer.Completed += () => count++;
    timer.Start(); clock.Advance(31); timer.Tick(); timer.Tick(); timer.Pause(); timer.Resume(); Equal(1,count); Equal(TimerState.Completed,timer.State); Equal(TimeSpan.Zero,timer.Remaining);
});
Test("Completed timer can restart", () =>
{
    var clock=new FakeClock(); var timer=new TestTimer(clock); timer.Start(); clock.Advance(30); timer.Tick(); timer.Start(); Equal(30d,timer.Remaining.TotalSeconds);
});
Test("Pause at deadline finalizes instead of creating paused zero", () =>
{
    var clock=new FakeClock(); var timer=new TestTimer(clock); timer.Start(); clock.Advance(30); timer.Pause(); Equal(TimerState.Completed,timer.State);
});
Test("Tray actions share timer and window operations", () =>
{
    var clock=new FakeClock(); var timer=new TestTimer(clock); var opens=0; var exits=0; NotificationRequest? sent=null;
    var actions=new SpikeActions(timer,()=>opens++,()=>exits++,r=>sent=r);
    actions.Start(); actions.TogglePause(); Equal(TimerState.Paused,timer.State); actions.TogglePause(); Equal(TimerState.Running,timer.State);
    clock.Advance(5); actions.Open(); actions.Open(); Equal(25d,timer.Remaining.TotalSeconds);
    actions.Exit(); Equal(2,opens); Equal(1,exits);
    actions.SendTestNotification(); Equal("manual",sent!.Source); actions.TimerCompleted(); Equal("timer",sent.Source);
});
Test("Notification event attached before Register; cleanup once", () =>
{
    var backend=new FakeNotifications(); var activations=0;
    var session=new NotificationSession(backend,()=>activations++,_=>{});
    Equal(true,session.Initialize()); Equal(true,session.Initialize()); backend.Activate(); Equal(1,activations);
    session.Send(new("title","body","manual")); Equal(1,backend.Sends);
    session.Dispose(); session.Dispose(); Equal(1,backend.Unregisters); backend.Activate(); Equal(1,activations);
});
Test("Notification registration failure is reported and contained", () =>
{
    var backend=new FakeNotifications { FailRegistration=true }; var reports=new List<string>();
    using var session=new NotificationSession(backend,()=>{},reports.Add);
    Equal(false,session.Initialize()); session.Send(new("a","b","manual")); Equal(0,backend.Sends); Equal(2,reports.Count);
});
Test("Notification send failure cannot stop timer completion", () =>
{
    var backend=new FakeNotifications { FailSend=true }; var reports=new List<string>();
    using var session=new NotificationSession(backend,()=>{},reports.Add); session.Initialize();
    var clock=new FakeClock(); var timer=new TestTimer(clock); timer.Completed+=()=>session.Send(new("a","b","timer"));
    timer.Start(); clock.Advance(30); timer.Tick(); Equal(TimerState.Completed,timer.State); Equal(2,reports.Count);
});
Test("Notification unregister failure is contained", () =>
{
    var backend=new FakeNotifications { FailUnregister=true }; var reports=new List<string>();
    var session=new NotificationSession(backend,()=>{},reports.Add); session.Initialize(); session.Dispose(); Equal(2,reports.Count);
});

var failed=0;
foreach (var (name,run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"TOTAL {tests.Count}; PASSED {tests.Count-failed}; FAILED {failed}");
Console.WriteLine("NOT TESTED: real WPF window, system tray, notification display/activation, installation, Windows 11 25H2 desktop.");
return failed == 0 ? 0 : 1;

sealed class FakeClock : IMonotonicClock
{
    public TimeSpan Now { get; private set; }
    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}
sealed class FakeNotifications : INotificationBackend
{
    public event Action? Invoked;
    public bool FailRegistration, FailSend, FailUnregister;
    public int Sends, Unregisters;
    public void Register()
    {
        if (Invoked == null) throw new Exception("No event attached before Register");
        if (FailRegistration) throw new Exception("simulated registration failure");
    }
    public void Show(NotificationRequest request) { if (FailSend) throw new Exception("simulated send failure"); Sends++; }
    public void Unregister() { Unregisters++; if (FailUnregister) throw new Exception("simulated cleanup failure"); }
    public void Activate() => Invoked?.Invoke();
}
