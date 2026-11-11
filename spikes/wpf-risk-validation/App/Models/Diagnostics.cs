using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace PomodoroTimer.Spike.Models;

public static class Diagnostics
{
    public static string NativeModules()
    {
        using var process = Process.GetCurrentProcess();
        var text = new System.Text.StringBuilder();
        foreach (ProcessModule module in process.Modules)
        {
            if (module.ModuleName.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase) ||
                module.ModuleName.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase) ||
                module.ModuleName.Contains("WindowsAppRuntime", StringComparison.OrdinalIgnoreCase))
                text.AppendLine($"Loaded native module: {module.ModuleName} {module.FileVersionInfo.FileVersion} {module.FileName}");
        }
        return text.ToString();
    }

    public static string Describe()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        return $"Windows {key?.GetValue("DisplayVersion")} · OS Build {key?.GetValue("CurrentBuildNumber")}.{key?.GetValue("UBR")}\n"
            + $"{RuntimeInformation.OSArchitecture} / 进程 {RuntimeInformation.ProcessArchitecture} · "
            + $"{(elevated ? "管理员权限（通知不支持）" : "普通权限")}\n"
            + $"应用 {Assembly.GetExecutingAssembly().GetName().Version} · .NET {Environment.Version}\n"
            + "Windows App SDK 2.6.5 · TFM net10.0-windows10.0.26100.0\n"
            + $"PID {Environment.ProcessId} · 正式验收仅 Windows 11 25H2 x64";
    }
}
