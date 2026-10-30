using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OptimAdhan.App.Services;

/// <summary>Start with Windows via the per-user Run key (no admin rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OptimAdhan";

    public const string BackgroundArgument = "--background";

    public static void Apply(bool enabled)
    {
        if (DevMode.IsEnabled)
        {
            return; // never touch the real Run key while developing
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
            {
                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArgument}");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not update start-with-Windows setting", ex);
        }
    }
}

/// <summary>
/// Developer mode (environment variable OPTIMADHAN_DEV=1): settings go to a
/// separate folder and start-up registration is skipped, so testing leaves
/// no trace on the machine.
/// </summary>
public static class DevMode
{
    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable("OPTIMADHAN_DEV") == "1";

    public static string SettingsPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optim Square", "OptimAdhan-dev", "settings.json");
}

/// <summary>Detects full-screen apps, games and presentations (shell notification state).</summary>
public static class FullScreenDetector
{
    private enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    public static bool IsFullScreenActive()
    {
        try
        {
            // OptimAdhan's own full-screen dashboard is not a reason to stay quiet.
            if (GetWindowThreadProcessId(GetForegroundWindow(), out var pid) != 0 && pid == Environment.ProcessId)
            {
                return false;
            }

            return SHQueryUserNotificationState(out var state) == 0
                && state is UserNotificationState.Busy
                    or UserNotificationState.RunningDirect3DFullScreen
                    or UserNotificationState.PresentationMode;
        }
        catch (Exception ex)
        {
            Log.Error("SHQueryUserNotificationState failed", ex);
            return false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
}

/// <summary>Only one OptimAdhan per user session; a second launch asks the first to show itself.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string Name = "OptimSquare.OptimAdhan";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}.Instance", out var createdNew);
        IsFirstInstance = createdNew;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{Name}.Activate");
    }

    public bool IsFirstInstance { get; }

    /// <summary>Called on a thread-pool thread when another launch asks to activate.</summary>
    public void ListenForActivation(Action onActivate) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void SignalFirstInstance() => _activate.Set();

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate.Dispose();
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
