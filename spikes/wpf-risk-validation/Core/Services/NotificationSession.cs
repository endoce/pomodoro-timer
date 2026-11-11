using PomodoroTimer.Spike.Core.Models;

namespace PomodoroTimer.Spike.Core.Services;

public interface INotificationBackend
{
    event Action? Invoked;
    void Register();
    void Show(NotificationRequest request);
    void Unregister();
}

// Testable registration order and failure boundary, without faking a Windows
// desktop notification result. The Windows backend owns SDK registration.
public sealed class NotificationSession(INotificationBackend backend, Action activate,
    Action<string> report) : IDisposable
{
    private bool subscribed;
    private bool registered;
    private bool disposed;

    public bool Initialize()
    {
        if (disposed) return false;
        if (registered) return true;
        if (!subscribed) { backend.Invoked += activate; subscribed = true; }
        try
        {
            backend.Register();
            registered = true;
            report("通知注册成功；显示及点击仍需桌面验证。");
            return true;
        }
        catch (Exception error)
        {
            report($"通知注册失败：{error.GetType().Name}，HRESULT 0x{error.HResult:X8}");
            return false;
        }
    }

    public void Send(NotificationRequest request)
    {
        if (!registered) { report("通知不可用：未成功注册。"); return; }
        try
        {
            backend.Show(request);
            report("通知已提交到 Windows；横幅显示、点击激活分别需要桌面验证。");
        }
        catch (Exception error)
        {
            report($"通知发送失败：{error.GetType().Name}，HRESULT 0x{error.HResult:X8}");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed) { backend.Invoked -= activate; subscribed = false; }
        if (!registered) return;
        try { backend.Unregister(); }
        catch (Exception error) { report($"通知注销失败：HRESULT 0x{error.HResult:X8}"); }
        registered = false;
    }
}
